using System;

namespace CraftyRacoon.PackageManagement.PackageSets
{
    public enum PackageSetStatus
    {
        NotConfigured,
        Missing,
        Invalid,
        Available,
        Ready
    }

    public sealed class PackageSetDiscoverySnapshot
    {
        public PackageSetDiscoverySnapshot(
            PackageSetBinding binding,
            PackageSetStatus status,
            string detail,
            bool manifestValid,
            bool requiredSourcesInitialized,
            bool exactRevisionAligned)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            Status = status;
            Detail = detail ?? string.Empty;
            ManifestValid = manifestValid;
            RequiredSourcesInitialized = requiredSourcesInitialized;
            ExactRevisionAligned = exactRevisionAligned;
        }

        public PackageSetBinding Binding { get; }
        public PackageSetStatus Status { get; }
        public string Detail { get; }
        public bool ManifestValid { get; }
        public bool RequiredSourcesInitialized { get; }
        public bool ExactRevisionAligned { get; }

        public static PackageSetDiscoverySnapshot NotConfigured()
        {
            return new PackageSetDiscoverySnapshot(
                new PackageSetBinding(string.Empty, string.Empty),
                PackageSetStatus.NotConfigured,
                "No PackageSet binding is configured.",
                false,
                false,
                false);
        }
    }
}
