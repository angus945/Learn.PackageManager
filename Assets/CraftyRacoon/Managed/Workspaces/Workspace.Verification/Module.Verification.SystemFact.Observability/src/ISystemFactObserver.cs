using Module.Verification.SystemFact;

namespace Module.Verification.SystemFact.Observability
{
    public interface ISystemFactObserver<in TFact> where TFact : ISystemFact
    {
        void Observe(TFact fact, FactObservationContext context);
    }
}
