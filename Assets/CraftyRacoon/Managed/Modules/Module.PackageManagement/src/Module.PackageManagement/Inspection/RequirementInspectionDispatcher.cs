using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Domain;

namespace CraftyRacoon.PackageManagement.Inspection
{
    public sealed class RequirementInspectionDispatcher
    {
        private readonly IReadOnlyDictionary<string, IRequirementInspector> inspectors;

        public RequirementInspectionDispatcher(IEnumerable<IRequirementInspector> inspectors)
        {
            if (inspectors == null)
                throw new ArgumentNullException(nameof(inspectors));

            Dictionary<string, IRequirementInspector> map =
                new Dictionary<string, IRequirementInspector>(StringComparer.Ordinal);

            foreach (IRequirementInspector inspector in inspectors)
            {
                if (inspector == null)
                    throw new ArgumentException("Inspector collection contains null.", nameof(inspectors));
                if (string.IsNullOrWhiteSpace(inspector.ProviderId))
                    throw new ArgumentException("Inspector provider id is required.", nameof(inspectors));
                if (!map.TryAdd(inspector.ProviderId, inspector))
                    throw new ArgumentException(
                        "Duplicate requirement inspector provider id: " + inspector.ProviderId,
                        nameof(inspectors));
            }

            this.inspectors = map;
        }

        public ManagedRequirementSnapshot Inspect(ManagedPackageRequirement requirement)
        {
            if (requirement == null)
                throw new ArgumentNullException(nameof(requirement));

            if (!inspectors.TryGetValue(requirement.ProviderId, out IRequirementInspector? inspector))
            {
                return new ManagedRequirementSnapshot(
                    requirement,
                    ManagedRequirementState.Unsupported,
                    "No requirement inspector is registered for provider '" +
                    requirement.ProviderId + "'.");
            }

            return inspector.Inspect(requirement);
        }
    }
}
