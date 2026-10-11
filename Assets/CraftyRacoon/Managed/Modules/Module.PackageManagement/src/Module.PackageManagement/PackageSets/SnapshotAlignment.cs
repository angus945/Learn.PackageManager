namespace CraftyRacoon.PackageManagement.PackageSets
{
    public enum SnapshotAlignmentState
    {
        Unknown,
        Aligned,
        SourceDirty,
        SourcePinMismatch,
        ProjectionMismatch
    }

    public sealed class SnapshotAlignment
    {
        public SnapshotAlignment(
            string sourceRevision,
            string packageSetPin,
            SnapshotAlignmentState state,
            string detail)
        {
            SourceRevision = sourceRevision ?? string.Empty;
            PackageSetPin = packageSetPin ?? string.Empty;
            State = state;
            Detail = detail ?? string.Empty;
        }

        public string SourceRevision { get; }
        public string PackageSetPin { get; }
        public SnapshotAlignmentState State { get; }
        public string Detail { get; }
    }
}
