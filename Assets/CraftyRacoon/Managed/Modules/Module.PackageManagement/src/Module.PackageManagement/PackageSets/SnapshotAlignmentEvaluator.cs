using System;

namespace CraftyRacoon.PackageManagement.PackageSets
{
    public static class SnapshotAlignmentEvaluator
    {
        public static SnapshotAlignment Evaluate(
            string sourceRevision,
            string packageSetPin,
            bool sourceDirty,
            bool projectionMatchesSourceAtPin)
        {
            sourceRevision = sourceRevision ?? string.Empty;
            packageSetPin = packageSetPin ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sourceRevision) ||
                string.IsNullOrWhiteSpace(packageSetPin))
            {
                return new SnapshotAlignment(
                    sourceRevision,
                    packageSetPin,
                    SnapshotAlignmentState.Unknown,
                    "Source revision or PackageSet pin is unavailable.");
            }

            if (sourceDirty)
            {
                return new SnapshotAlignment(
                    sourceRevision,
                    packageSetPin,
                    SnapshotAlignmentState.SourceDirty,
                    "Canonical source contains uncommitted changes.");
            }

            if (!string.Equals(sourceRevision, packageSetPin, StringComparison.OrdinalIgnoreCase))
            {
                return new SnapshotAlignment(
                    sourceRevision,
                    packageSetPin,
                    SnapshotAlignmentState.SourcePinMismatch,
                    "Canonical source HEAD does not match the PackageSet exact pin.");
            }

            if (!projectionMatchesSourceAtPin)
            {
                return new SnapshotAlignment(
                    sourceRevision,
                    packageSetPin,
                    SnapshotAlignmentState.ProjectionMismatch,
                    "Distribution projection does not match canonical source at the exact pin.");
            }

            return new SnapshotAlignment(
                sourceRevision,
                packageSetPin,
                SnapshotAlignmentState.Aligned,
                "Canonical source, PackageSet pin, and distribution projection are aligned.");
        }
    }
}
