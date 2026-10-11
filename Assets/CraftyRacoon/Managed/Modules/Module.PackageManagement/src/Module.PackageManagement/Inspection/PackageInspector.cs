using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Domain;

namespace CraftyRacoon.PackageManagement.Inspection
{
    public sealed class PackageInspector
    {
        private readonly RequirementInspectionDispatcher dispatcher;

        public PackageInspector(RequirementInspectionDispatcher dispatcher)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public ManagedPackageSnapshot Inspect(ManagedPackageDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (definition.Availability == ManagedPackageAvailability.Planned)
            {
                return new ManagedPackageSnapshot(
                    definition,
                    ManagedPackageState.Unsupported,
                    "Package is registered but not currently available.");
            }

            List<ManagedRequirementSnapshot> snapshots =
                new List<ManagedRequirementSnapshot>(definition.Requirements.Count);

            for (int index = 0; index < definition.Requirements.Count; index++)
                snapshots.Add(dispatcher.Inspect(definition.Requirements[index]));

            ManagedPackageState state = PackageStateReducer.Reduce(definition, snapshots);
            return new ManagedPackageSnapshot(
                definition,
                state,
                Describe(state, definition.SupportsInstallation),
                snapshots.AsReadOnly());
        }

        private static string Describe(ManagedPackageState state, bool supportsInstallation)
        {
            switch (state)
            {
                case ManagedPackageState.Installed:
                    return "All declared package requirements are satisfied.";
                case ManagedPackageState.NotInstalled:
                    return supportsInstallation
                        ? "Package is not installed and has a declared installation source."
                        : "Package is not installed and no complete installation source is declared.";
                case ManagedPackageState.Incomplete:
                    return "Some declared package requirements are satisfied and some are missing.";
                case ManagedPackageState.Conflict:
                    return "Observed state conflicts with the declared package composition.";
                case ManagedPackageState.Unsupported:
                    return "Package or one of its requirement providers is unsupported by this host.";
                default:
                    return "Package state could not be proven from the current host.";
            }
        }
    }
}
