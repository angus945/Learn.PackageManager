using System;

namespace CraftyRacoon.PackageManagement.Framework.Snapshot
{
    public sealed class ManagedSnapshotSubject
    {
        public ManagedSnapshotSubject(
            string identity,
            string canonicalSourceLocation,
            string distributionProjectionLocation,
            string exactPin)
        {
            if (string.IsNullOrWhiteSpace(identity))
                throw new ArgumentException("Snapshot subject identity is required.", nameof(identity));

            Identity = identity;
            CanonicalSourceLocation = canonicalSourceLocation ?? string.Empty;
            DistributionProjectionLocation = distributionProjectionLocation ?? string.Empty;
            ExactPin = exactPin ?? string.Empty;
        }

        public string Identity { get; }
        public string CanonicalSourceLocation { get; }
        public string DistributionProjectionLocation { get; }
        public string ExactPin { get; }
    }
}
