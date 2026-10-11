using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Framework.Ports;
using CraftyRacoon.PackageManagement.PackageSets;

namespace CraftyRacoon.PackageManagement.Framework.Snapshot
{
    public sealed class SnapshotObservationWorkflow
    {
        private readonly IPackageSetSnapshotInventoryReader inventoryReader;
        private readonly ICanonicalSourceInspector sourceInspector;
        private readonly IDistributionProjectionInspector projectionInspector;

        public SnapshotObservationWorkflow(
            IPackageSetSnapshotInventoryReader inventoryReader,
            ICanonicalSourceInspector sourceInspector,
            IDistributionProjectionInspector projectionInspector)
        {
            this.inventoryReader = inventoryReader ?? throw new ArgumentNullException(nameof(inventoryReader));
            this.sourceInspector = sourceInspector ?? throw new ArgumentNullException(nameof(sourceInspector));
            this.projectionInspector = projectionInspector ?? throw new ArgumentNullException(nameof(projectionInspector));
        }

        public IReadOnlyList<SnapshotObservationResult> Execute(
            SnapshotObservationRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            IReadOnlyList<ManagedSnapshotSubject> subjects =
                inventoryReader.Read(request.TargetRoot, request.PackageSetRoot)
                ?? Array.Empty<ManagedSnapshotSubject>();

            List<SnapshotObservationResult> results =
                new List<SnapshotObservationResult>(subjects.Count);

            for (int index = 0; index < subjects.Count; index++)
            {
                ManagedSnapshotSubject subject = subjects[index]
                    ?? throw new InvalidOperationException("Snapshot inventory contains a null subject.");

                CanonicalSourceObservation source = sourceInspector.Inspect(subject)
                    ?? throw new InvalidOperationException(
                        "Canonical source inspector returned null for '" + subject.Identity + "'.");

                bool mayInspectProjection =
                    !source.IsDirty &&
                    !string.IsNullOrWhiteSpace(source.Revision) &&
                    !string.IsNullOrWhiteSpace(subject.ExactPin) &&
                    string.Equals(
                        source.Revision,
                        subject.ExactPin,
                        StringComparison.OrdinalIgnoreCase);

                DistributionProjectionObservation? projection = null;
                if (mayInspectProjection)
                {
                    projection = projectionInspector.Inspect(subject, subject.ExactPin)
                        ?? throw new InvalidOperationException(
                            "Distribution projection inspector returned null for '" +
                            subject.Identity + "'.");
                }

                SnapshotAlignment alignment = SnapshotAlignmentEvaluator.Evaluate(
                    source.Revision,
                    subject.ExactPin,
                    source.IsDirty,
                    projection != null && projection.MatchesSourceAtPin);

                results.Add(
                    new SnapshotObservationResult(
                        subject,
                        alignment,
                        source.Detail,
                        projection?.Detail ?? "Projection comparison skipped until source HEAD is clean and aligned with the PackageSet exact pin."));
            }

            return results.AsReadOnly();
        }
    }
}
