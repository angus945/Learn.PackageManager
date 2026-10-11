using System;
using System.Collections.Generic;

namespace CraftyRacoon.PackageManagement.Domain
{
    public enum ManagedRequirementState
    {
        Satisfied,
        Missing,
        Mismatch,
        Error,
        Unsupported
    }

    public enum ManagedPackageState
    {
        Unknown,
        NotInstalled,
        Installed,
        Incomplete,
        Conflict,
        Unsupported
    }

    public sealed class ManagedRequirementSnapshot
    {
        public ManagedRequirementSnapshot(
            ManagedPackageRequirement requirement,
            ManagedRequirementState state,
            string detail)
        {
            Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
            State = state;
            Detail = detail ?? string.Empty;
        }

        public ManagedPackageRequirement Requirement { get; }
        public ManagedRequirementState State { get; }
        public string Detail { get; }
    }

    public sealed class ManagedPackageSnapshot
    {
        public ManagedPackageSnapshot(
            ManagedPackageDefinition definition,
            ManagedPackageState state,
            string detail,
            IReadOnlyList<ManagedRequirementSnapshot>? requirements = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            State = state;
            Detail = detail ?? string.Empty;
            Requirements = requirements ?? Array.Empty<ManagedRequirementSnapshot>();
        }

        public ManagedPackageDefinition Definition { get; }
        public ManagedPackageState State { get; }
        public string Detail { get; }
        public IReadOnlyList<ManagedRequirementSnapshot> Requirements { get; }
    }
}
