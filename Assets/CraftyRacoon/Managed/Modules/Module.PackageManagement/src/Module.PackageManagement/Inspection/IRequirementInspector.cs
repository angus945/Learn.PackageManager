using CraftyRacoon.PackageManagement.Domain;

namespace CraftyRacoon.PackageManagement.Inspection
{
    public interface IRequirementInspector
    {
        string ProviderId { get; }

        ManagedRequirementSnapshot Inspect(ManagedPackageRequirement requirement);
    }
}
