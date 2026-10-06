using System;
using System.Collections.Generic;
using Module.Verification.SystemFact;

namespace Module.Verification.SystemFact.Observability
{
    internal interface ISystemFactRoute
    {
        Type FactType { get; }
        object Observer { get; }
        void Observe(ISystemFact fact, FactObservationContext context);
    }

    internal sealed class SystemFactRoute<TFact> : ISystemFactRoute where TFact : ISystemFact
    {
        private readonly ISystemFactObserver<TFact> observer;

        public SystemFactRoute(ISystemFactObserver<TFact> observer)
        {
            this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public Type FactType
        {
            get { return typeof(TFact); }
        }

        public object Observer
        {
            get { return observer; }
        }

        public void Observe(ISystemFact fact, FactObservationContext context)
        {
            observer.Observe((TFact)fact, context);
        }
    }

    public sealed class SystemFactHubBuilder
    {
        private readonly List<ISystemFactRoute> routes = new List<ISystemFactRoute>();
        private IObservationFailureSink failureSink;
        private bool built;

        public SystemFactHubBuilder Register<TFact>(ISystemFactObserver<TFact> observer) where TFact : ISystemFact
        {
            EnsureMutable();
            routes.Add(new SystemFactRoute<TFact>(observer));
            return this;
        }

        public SystemFactHubBuilder ReportFailuresTo(IObservationFailureSink sink)
        {
            EnsureMutable();
            failureSink = sink ?? throw new ArgumentNullException(nameof(sink));
            return this;
        }

        public SystemFactHub Build()
        {
            EnsureMutable();
            built = true;
            ISystemFactRoute[] frozenRoutes = routes.ToArray();
            IObservationFailureSink selectedSink = failureSink ?? NullObservationFailureSink.Instance;
            return new SystemFactHub(frozenRoutes, selectedSink);
        }

        private void EnsureMutable()
        {
            if (built) throw new InvalidOperationException("A SystemFactHubBuilder cannot be changed after Build.");
        }
    }

    public sealed class SystemFactHub : ISystemFactSink
    {
        private readonly object sequenceGate = new object();
        private readonly IReadOnlyList<ISystemFactRoute> routes;
        private readonly IObservationFailureSink failureSink;
        private long sequence;

        internal SystemFactHub(IEnumerable<ISystemFactRoute> routes, IObservationFailureSink failureSink)
        {
            this.routes = new List<ISystemFactRoute>(routes ?? throw new ArgumentNullException(nameof(routes))).AsReadOnly();
            this.failureSink = failureSink ?? throw new ArgumentNullException(nameof(failureSink));
        }

        public void Publish(ISystemFact fact)
        {
            if (fact == null) throw new ArgumentNullException(nameof(fact));
            long publishSequence = NextSequence();
            FactObservationContext context = new FactObservationContext(publishSequence, DateTimeOffset.UtcNow);
            Type factType = fact.GetType();

            foreach (ISystemFactRoute route in routes)
            {
                if (!route.FactType.IsAssignableFrom(factType)) continue;
                try
                {
                    route.Observe(fact, context);
                }
                catch (Exception exception)
                {
                    ObservationFailure failure = new ObservationFailure(fact, context, route.Observer, exception);
                    ReportFailure(failure);
                }
            }
        }

        private void ReportFailure(ObservationFailure failure)
        {
            try
            {
                failureSink.Report(failure);
            }
            catch (Exception)
            {
            }
        }

        private long NextSequence()
        {
            lock (sequenceGate)
            {
                sequence = checked(sequence + 1);
                return sequence;
            }
        }
    }
}
