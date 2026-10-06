using System.Collections.Generic;

namespace Module.Verification.SystemFact
{
    public interface ISystemFactSource
    {
        IReadOnlyCollection<ISystemFact> ReleaseFacts();
    }
}