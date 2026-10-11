using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Domain;

namespace CraftyRacoon.PackageManagement.Inspection
{
    public static class PackageStateReducer
    {
        public static ManagedPackageState Reduce(
            ManagedPackageDefinition definition,
            IReadOnlyList<ManagedRequirementSnapshot> requirements)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));

            if (definition.Availability == ManagedPackageAvailability.Planned)
                return ManagedPackageState.Unsupported;

            if (requirements.Count == 0)
                return ManagedPackageState.Unknown;

            int satisfied = 0;
            int missing = 0;

            for (int index = 0; index < requirements.Count; index++)
            {
                switch (requirements[index].State)
                {
                    case ManagedRequirementState.Satisfied:
                        satisfied++;
                        break;
                    case ManagedRequirementState.Missing:
                        missing++;
                        break;
                    case ManagedRequirementState.Mismatch:
                        return ManagedPackageState.Conflict;
                    case ManagedRequirementState.Error:
                        return ManagedPackageState.Unknown;
                    case ManagedRequirementState.Unsupported:
                        return ManagedPackageState.Unsupported;
                    default:
                        return ManagedPackageState.Unknown;
                }
            }

            if (satisfied == requirements.Count)
                return ManagedPackageState.Installed;
            if (missing == requirements.Count)
                return ManagedPackageState.NotInstalled;

            return ManagedPackageState.Incomplete;
        }
    }
}
