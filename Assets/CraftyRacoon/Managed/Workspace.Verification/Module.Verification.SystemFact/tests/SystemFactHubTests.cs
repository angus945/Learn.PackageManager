using System;
using System.Collections.Generic;
using Module.Verification.SystemFact;
using Module.Verification.SystemFact.Observability;
using Xunit;

namespace Module.Verification.SystemFact.Tests
{
    public sealed class SystemFactHubTests
    {
        private sealed class ExecutionFact : IExecutionFact
        {
        }

        private sealed class DomainFact : IDomainFact
        {
        }

        private sealed class CollectingObserver<TFact> : ISystemFactObserver<TFact> where TFact : ISystemFact
        {
            private readonly List<long> sequences = new List<long>();
            private readonly List<TFact> facts = new List<TFact>();

            public IReadOnlyList<long> Sequences
            {
                get { return sequences.AsReadOnly(); }
            }

            public IReadOnlyList<TFact> Facts
            {
                get { return facts.AsReadOnly(); }
            }

            public void Observe(TFact fact, FactObservationContext context)
            {
                facts.Add(fact);
                sequences.Add(context.Sequence);
            }
        }

        private sealed class ThrowingObserver : ISystemFactObserver<IExecutionFact>
        {
            public void Observe(IExecutionFact fact, FactObservationContext context)
            {
                throw new InvalidOperationException("observer failed");
            }
        }

        private sealed class CollectingFailureSink : IObservationFailureSink
        {
            private readonly List<ObservationFailure> failures = new List<ObservationFailure>();

            public IReadOnlyList<ObservationFailure> Failures
            {
                get { return failures.AsReadOnly(); }
            }

            public void Report(ObservationFailure failure)
            {
                failures.Add(failure);
            }
        }

        private sealed class ThrowingFailureSink : IObservationFailureSink
        {
            public void Report(ObservationFailure failure)
            {
                throw new InvalidOperationException("failure sink failed");
            }
        }

        [Fact]
        public void AssignableRoutesReceiveConcreteFactsAndPublishSequenceIncludesUnobservedFacts()
        {
            CollectingObserver<IExecutionFact> observer = new CollectingObserver<IExecutionFact>();
            SystemFactHubBuilder builder = new SystemFactHubBuilder();
            builder.Register<IExecutionFact>(observer);
            SystemFactHub hub = builder.Build();

            hub.Publish(new DomainFact());
            hub.Publish(new ExecutionFact());

            Assert.Equal(new long[] { 2 }, observer.Sequences);
        }

        [Fact]
        public void NullFactFailsDeterministically()
        {
            SystemFactHub hub = new SystemFactHubBuilder().Build();
            ArgumentNullException captured = null;

            try
            {
                hub.Publish(null);
            }
            catch (ArgumentNullException exception)
            {
                captured = exception;
            }

            Assert.NotNull(captured);
            Assert.Equal("fact", captured.ParamName);
        }

        [Fact]
        public void ObserverFailuresAreReportedWithoutStoppingOtherObservers()
        {
            CollectingFailureSink failureSink = new CollectingFailureSink();
            CollectingObserver<IExecutionFact> observer = new CollectingObserver<IExecutionFact>();
            SystemFactHubBuilder builder = new SystemFactHubBuilder();
            builder.ReportFailuresTo(failureSink);
            builder.Register<IExecutionFact>(new ThrowingObserver());
            builder.Register<IExecutionFact>(observer);
            SystemFactHub hub = builder.Build();

            hub.Publish(new ExecutionFact());

            Assert.Single(failureSink.Failures);
            Assert.Equal(new long[] { 1 }, observer.Sequences);
        }

        [Fact]
        public void FailureSinkFailuresDoNotAffectPublishing()
        {
            CollectingObserver<IExecutionFact> observer = new CollectingObserver<IExecutionFact>();
            SystemFactHubBuilder builder = new SystemFactHubBuilder();
            builder.ReportFailuresTo(new ThrowingFailureSink());
            builder.Register<IExecutionFact>(new ThrowingObserver());
            builder.Register<IExecutionFact>(observer);
            SystemFactHub hub = builder.Build();

            hub.Publish(new ExecutionFact());

            Assert.Equal(new long[] { 1 }, observer.Sequences);
        }

        [Fact]
        public void BuilderFreezesAfterBuild()
        {
            SystemFactHubBuilder builder = new SystemFactHubBuilder();
            builder.Build();
            InvalidOperationException captured = null;

            try
            {
                builder.Register<IExecutionFact>(new CollectingObserver<IExecutionFact>());
            }
            catch (InvalidOperationException exception)
            {
                captured = exception;
            }

            Assert.NotNull(captured);
        }

        [Fact]
        public void BaseConcreteAndSystemFactObserversReceiveAssignableFactsWithSameSequence()
        {
            CollectingObserver<ExecutionFact> concrete = new CollectingObserver<ExecutionFact>();
            CollectingObserver<IExecutionFact> category = new CollectingObserver<IExecutionFact>();
            CollectingObserver<ISystemFact> system = new CollectingObserver<ISystemFact>();
            SystemFactHubBuilder builder = new SystemFactHubBuilder();
            builder.Register<ExecutionFact>(concrete);
            builder.Register<IExecutionFact>(category);
            builder.Register<ISystemFact>(system);
            SystemFactHub hub = builder.Build();
            ExecutionFact fact = new ExecutionFact();

            hub.Publish(fact);

            Assert.Same(fact, concrete.Facts[0]);
            Assert.Same(fact, category.Facts[0]);
            Assert.Same(fact, system.Facts[0]);
            Assert.Equal(category.Sequences[0], concrete.Sequences[0]);
            Assert.Equal(system.Sequences[0], category.Sequences[0]);
        }

        [Fact]
        public void SequenceAdvancesOnEveryAcceptedPublishWithoutObservers()
        {
            SystemFactHub unobservedHub = new SystemFactHubBuilder().Build();
            unobservedHub.Publish(new DomainFact());
            unobservedHub.Publish(new ExecutionFact());

            CollectingObserver<ISystemFact> observer = new CollectingObserver<ISystemFact>();
            SystemFactHubBuilder builder = new SystemFactHubBuilder();
            builder.Register<ISystemFact>(observer);
            SystemFactHub observedHub = builder.Build();
            observedHub.Publish(new DomainFact());
            observedHub.Publish(new ExecutionFact());

            Assert.Equal(new long[] { 1, 2 }, observer.Sequences);
        }

        [Fact]
        public void ObserverFailureReportPreservesFactObserverExceptionAndContext()
        {
            ThrowingObserver observer = new ThrowingObserver();
            CollectingFailureSink failureSink = new CollectingFailureSink();
            SystemFactHubBuilder builder = new SystemFactHubBuilder();
            builder.ReportFailuresTo(failureSink);
            builder.Register<IExecutionFact>(observer);
            SystemFactHub hub = builder.Build();
            ExecutionFact fact = new ExecutionFact();

            hub.Publish(fact);

            ObservationFailure failure = failureSink.Failures[0];
            Assert.Same(fact, failure.Fact);
            Assert.Same(observer, failure.Observer);
            Assert.Equal("observer failed", failure.Exception.Message);
            Assert.Equal(1, failure.Context.Sequence);
        }
    }
}
