using System;
using System.Collections.Generic;

namespace CraftyRacoon.PackageManagement.Domain
{
    public sealed class ManagedPackageDefinition
    {
        public ManagedPackageDefinition(
            string id,
            string displayName,
            string description,
            string category,
            string kind,
            ManagedPackageAvailability availability,
            bool supportsInstallation,
            IReadOnlyList<ManagedPackageRequirement>? requirements = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("Package display name is required.", nameof(displayName));

            Id = id;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            Category = category ?? string.Empty;
            Kind = kind ?? string.Empty;
            Availability = availability;
            SupportsInstallation = supportsInstallation;
            Requirements = requirements ?? Array.Empty<ManagedPackageRequirement>();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string Category { get; }
        public string Kind { get; }
        public ManagedPackageAvailability Availability { get; }
        public bool SupportsInstallation { get; }
        public IReadOnlyList<ManagedPackageRequirement> Requirements { get; }
    }

    public enum ManagedPackageAvailability
    {
        Available,
        Planned
    }
}
