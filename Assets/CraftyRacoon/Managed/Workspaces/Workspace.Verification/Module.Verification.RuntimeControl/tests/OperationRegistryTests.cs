using System.Collections.Generic;
using Xunit;

namespace Module.Verification.RuntimeControl.Tests
{
    public sealed class OperationRegistryTests
    {
        private sealed class Observer : IOperationTransitionObserver<string>
        {
            private readonly List<OperationTransition<string>> transitions = new List<OperationTransition<string>>();

            public IReadOnlyList<OperationTransition<string>> Transitions
            {
                get { return transitions.AsReadOnly(); }
            }

            public void Observe(OperationTransition<string> transition)
            {
                transitions.Add(transition);
            }
        }

        [Fact]
        public void AdmissionCreatesStableHandleAndReadReturnsAuthoritativeState()
        {
            OperationRegistry<string> registry = new OperationRegistry<string>("scope", 1);
            OperationDescriptor descriptor = new OperationDescriptor("move", "same");
            OperationAdmission first = registry.Admit(descriptor);
            OperationAdmission duplicate = registry.Admit(descriptor);

            Assert.Equal(OperationAdmissionStatus.Admitted, first.Status);
            Assert.Equal(first.Handle, duplicate.Handle);
            Assert.Equal(OperationState.Pending, registry.Read(first.Handle).State);
        }

        [Fact]
        public void RunningOperationCanCompleteAndTerminalOperationCannotTransitionAgain()
        {
            OperationRegistry<string> registry = new OperationRegistry<string>("scope", 1);
            OperationAdmission admission = registry.Admit(new OperationDescriptor("move"));

            Assert.True(registry.TryMarkRunning(admission.Handle));
            Assert.True(registry.TryComplete(admission.Handle, new OperationCompletion<string>(OperationState.Succeeded, "done", "result")));
            Assert.False(registry.TryComplete(admission.Handle, new OperationCompletion<string>(OperationState.Failed, "late")));
            Assert.False(registry.TryMarkRunning(admission.Handle));
        }

        [Theory]
        [InlineData(OperationState.Succeeded)]
        [InlineData(OperationState.Failed)]
        [InlineData(OperationState.Cancelled)]
        [InlineData(OperationState.Rejected)]
        public void CompletionStatesAreTerminal(OperationState terminalState)
        {
            OperationRegistry<string> registry = new OperationRegistry<string>("scope", 1);
            OperationAdmission admission = registry.Admit(new OperationDescriptor("move"));
            registry.TryComplete(admission.Handle, new OperationCompletion<string>(terminalState, "terminal"));

            Assert.Equal(terminalState, registry.Read(admission.Handle).State);
            Assert.False(registry.TryMarkRunning(admission.Handle));
        }

        [Fact]
        public void TransitionObserverDoesNotOwnRegistryState()
        {
            Observer observer = new Observer();
            OperationRegistry<string> registry = new OperationRegistry<string>("scope", 1, transitionObserver: observer);
            OperationAdmission admission = registry.Admit(new OperationDescriptor("move"));
            registry.TryMarkRunning(admission.Handle);
            registry.TryComplete(admission.Handle, new OperationCompletion<string>(OperationState.Succeeded, "done"));

            Assert.Equal(3, observer.Transitions.Count);
            Assert.Equal(OperationState.Pending, observer.Transitions[0].State);
            Assert.Equal(OperationState.Running, observer.Transitions[1].State);
            Assert.Equal(OperationState.Succeeded, observer.Transitions[2].State);
            Assert.Equal(OperationState.Succeeded, registry.Read(admission.Handle).State);
        }
    }
}
