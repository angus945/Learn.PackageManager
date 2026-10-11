using CraftyRacoon.PackageManagement.Framework.Snapshot;

namespace CraftyRacoon.PackageManagement.Framework.Ports
{
    public interface ICanonicalSourceInspector
    {
        CanonicalSourceObservation Inspect(ManagedSnapshotSubject subject);
    }

    public sealed class CanonicalSourceObservation
    {
        public CanonicalSourceObservation(
            string revision,
            bool isDirty,
            string detail)
        {
            Revision = revision ?? string.Empty;
            IsDirty = isDirty;
            Detail = detail ?? string.Empty;
        }

        public string Revision { get; }
        public bool IsDirty { get; }
        public string Detail { get; }
    }
}
