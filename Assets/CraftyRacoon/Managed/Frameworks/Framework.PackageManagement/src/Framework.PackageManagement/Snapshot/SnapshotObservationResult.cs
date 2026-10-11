using System;
using CraftyRacoon.PackageManagement.PackageSets;

namespace CraftyRacoon.PackageManagement.Framework.Snapshot
{
    public sealed class SnapshotObservationResult
    {
        public SnapshotObservationResult(
            ManagedSnapshotSubject subject,
            SnapshotAlignment alignment,
            string sourceObservationDetail,
            string projectionObservationDetail)
        {
            Subject = subject ?? throw new ArgumentNullException(nameof(subject));
            Alignment = alignment ?? throw new ArgumentNullException(nameof(alignment));
            SourceObservationDetail = sourceObservationDetail ?? string.Empty;
            ProjectionObservationDetail = projectionObservationDetail ?? string.Empty;
        }

        public ManagedSnapshotSubject Subject { get; }
        public SnapshotAlignment Alignment { get; }
        public string SourceObservationDetail { get; }
        public string ProjectionObservationDetail { get; }
    }
}
