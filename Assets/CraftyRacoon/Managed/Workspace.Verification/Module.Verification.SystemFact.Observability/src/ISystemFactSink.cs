using Module.Verification.SystemFact;

namespace Module.Verification.SystemFact.Observability
{
    public interface ISystemFactSink
    {
        void Publish(ISystemFact fact);
    }
}