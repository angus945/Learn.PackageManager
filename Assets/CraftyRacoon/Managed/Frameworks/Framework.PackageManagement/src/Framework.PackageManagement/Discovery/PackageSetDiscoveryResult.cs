using System;
using CraftyRacoon.PackageManagement.PackageSets;

namespace CraftyRacoon.PackageManagement.Framework.Discovery
{
    public sealed class PackageSetDiscoveryResult
    {
        public PackageSetDiscoveryResult(
            string targetRoot,
            PackageSetDiscoverySnapshot snapshot,
            string observationDetail)
        {
            TargetRoot = targetRoot ?? string.Empty;
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            ObservationDetail = observationDetail ?? string.Empty;
        }

        public string TargetRoot { get; }
        public PackageSetDiscoverySnapshot Snapshot { get; }
        public string ObservationDetail { get; }
    }
}
