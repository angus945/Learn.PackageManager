using System;

namespace CraftyRacoon.PackageManagement.Framework.Snapshot
{
    public sealed class SnapshotObservationRequest
    {
        public SnapshotObservationRequest(
            string targetRoot,
            string packageSetRoot)
        {
            if (string.IsNullOrWhiteSpace(targetRoot))
                throw new ArgumentException("Target root is required.", nameof(targetRoot));
            if (string.IsNullOrWhiteSpace(packageSetRoot))
                throw new ArgumentException("PackageSet root is required.", nameof(packageSetRoot));

            TargetRoot = targetRoot;
            PackageSetRoot = packageSetRoot;
        }

        public string TargetRoot { get; }
        public string PackageSetRoot { get; }
    }
}
