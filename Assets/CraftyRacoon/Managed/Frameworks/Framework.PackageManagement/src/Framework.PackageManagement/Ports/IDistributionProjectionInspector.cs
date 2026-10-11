using CraftyRacoon.PackageManagement.Framework.Snapshot;

namespace CraftyRacoon.PackageManagement.Framework.Ports
{
    public interface IDistributionProjectionInspector
    {
        DistributionProjectionObservation Inspect(
            ManagedSnapshotSubject subject,
            string exactPin);
    }

    public sealed class DistributionProjectionObservation
    {
        public DistributionProjectionObservation(
            bool matchesSourceAtPin,
            string detail)
        {
            MatchesSourceAtPin = matchesSourceAtPin;
            Detail = detail ?? string.Empty;
        }

        public bool MatchesSourceAtPin { get; }
        public string Detail { get; }
    }
}
