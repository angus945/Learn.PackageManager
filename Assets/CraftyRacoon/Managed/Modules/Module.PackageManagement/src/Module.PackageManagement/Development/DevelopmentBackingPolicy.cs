using System;
using CraftyRacoon.PackageManagement.PackageSets;

namespace CraftyRacoon.PackageManagement.Development
{
    public enum DevelopmentProjectionState { Absent, PhysicalSnapshot, BootstrapGitlink, CorrectJunction, Conflict }
    public enum DevelopmentBackingDecision { ReadyForOfflineApply, AlreadyConfigured, Blocked, Conflict }

    public sealed class DevelopmentSourceObservation
    {
        public DevelopmentSourceObservation(bool sourceAvailable, string sourceHead, string exactPin, bool sourceDirty, bool contentMatchesExactPin, bool projectHasIndependentChanges, bool metadataOwnershipVerified, bool pathOwnershipVerified, bool indexAlignmentVerified, DevelopmentProjectionState projectionState, string evidenceToken)
        {
            SourceAvailable = sourceAvailable;
            SourceHead = sourceHead ?? string.Empty;
            ExactPin = exactPin ?? string.Empty;
            SourceDirty = sourceDirty;
            ContentMatchesExactPin = contentMatchesExactPin;
            ProjectHasIndependentChanges = projectHasIndependentChanges;
            MetadataOwnershipVerified = metadataOwnershipVerified;
            PathOwnershipVerified = pathOwnershipVerified;
            IndexAlignmentVerified = indexAlignmentVerified;
            ProjectionState = projectionState;
            EvidenceToken = evidenceToken ?? string.Empty;
        }

        public bool SourceAvailable { get; }
        public string SourceHead { get; }
        public string ExactPin { get; }
        public bool SourceDirty { get; }
        public bool ContentMatchesExactPin { get; }
        public bool ProjectHasIndependentChanges { get; }
        public bool MetadataOwnershipVerified { get; }
        public bool PathOwnershipVerified { get; }
        public bool IndexAlignmentVerified { get; }
        public DevelopmentProjectionState ProjectionState { get; }
        public string EvidenceToken { get; }
    }

    public sealed class DevelopmentBackingAssessment
    {
        internal DevelopmentBackingAssessment(DevelopmentBackingDecision decision, bool needsPublish, string reason)
        {
            Decision = decision;
            NeedsPublish = needsPublish;
            Reason = reason;
        }

        public DevelopmentBackingDecision Decision { get; }
        public bool NeedsPublish { get; }
        public string Reason { get; }
        public bool CanProceed
        {
            get
            {
                bool readyForApply = Decision == DevelopmentBackingDecision.ReadyForOfflineApply;
                bool alreadyConfigured = Decision == DevelopmentBackingDecision.AlreadyConfigured;
                return readyForApply || alreadyConfigured;
            }
        }
    }

    public static class DevelopmentBackingPolicy
    {
        public static DevelopmentBackingAssessment Evaluate(DevelopmentSourceKind kind, DevelopmentSourceObservation observation)
        {
            if (!Enum.IsDefined(typeof(DevelopmentSourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            if (!Enum.IsDefined(typeof(DevelopmentProjectionState), observation.ProjectionState)) throw new ArgumentOutOfRangeException(nameof(observation));

            bool localDraft = kind == DevelopmentSourceKind.LocalDraft;
            bool needsPublish = localDraft && observation.ProjectionState == DevelopmentProjectionState.PhysicalSnapshot;

            if (observation.ProjectionState == DevelopmentProjectionState.Conflict)
                return Result(needsPublish, DevelopmentBackingDecision.Conflict, "The projection is occupied by conflicting data or a different backing.");
            if (!observation.PathOwnershipVerified)
                return Result(needsPublish, DevelopmentBackingDecision.Conflict, "Projection path ownership is invalid or overlaps unrelated Project data.");
            if (!observation.MetadataOwnershipVerified)
                return Result(needsPublish, DevelopmentBackingDecision.Conflict, "Projection metadata ownership is invalid or ambiguous.");
            if (!observation.IndexAlignmentVerified)
                return Result(needsPublish, DevelopmentBackingDecision.Conflict, "Project Git index ownership or content is not aligned with the observed projection.");
            if (observation.ProjectHasIndependentChanges)
                return Result(needsPublish, DevelopmentBackingDecision.Conflict, "Project-only changes must be resolved before conversion.");

            if (!observation.SourceAvailable || string.IsNullOrWhiteSpace(observation.EvidenceToken))
                return Result(needsPublish, DevelopmentBackingDecision.Blocked, "Source or content evidence is unavailable.");
            if (localDraft && observation.ProjectionState != DevelopmentProjectionState.PhysicalSnapshot)
                return Result(false, DevelopmentBackingDecision.Blocked, "LocalDraft requires a physical snapshot and cannot represent an absent, Gitlink, or Junction projection.");

            // An unpublished source has a local committed baseline, not a portable PackageSet gitlink.
            string pin = localDraft ? observation.SourceHead : observation.ExactPin;
            SnapshotAlignment alignment = SnapshotAlignmentEvaluator.Evaluate(observation.SourceHead, pin, observation.SourceDirty, observation.ContentMatchesExactPin);
            if (alignment.State != SnapshotAlignmentState.Aligned)
                return Result(needsPublish, DevelopmentBackingDecision.Blocked, "Source, pin, or content is not aligned: " + alignment.State);
            if (observation.ProjectionState == DevelopmentProjectionState.CorrectJunction)
                return Result(false, DevelopmentBackingDecision.AlreadyConfigured, "The projection already targets the exact canonical source.");
            if (observation.ProjectionState == DevelopmentProjectionState.Absent)
                return Result(false, DevelopmentBackingDecision.ReadyForOfflineApply, "The owned projection path is absent and can be materialized during offline Apply.");
            if (observation.ProjectionState == DevelopmentProjectionState.PhysicalSnapshot || observation.ProjectionState == DevelopmentProjectionState.BootstrapGitlink)
            {
                string reason = localDraft
                    ? "Local development only. Publish the source before promising fresh-clone portability."
                    : "Source, exact pin, content, metadata ownership, and path ownership agree.";
                return Result(needsPublish, DevelopmentBackingDecision.ReadyForOfflineApply, reason);
            }

            throw new InvalidOperationException("The projection state was not classified.");
        }

        private static DevelopmentBackingAssessment Result(bool needsPublish, DevelopmentBackingDecision decision, string reason)
        {
            return new DevelopmentBackingAssessment(decision, needsPublish, reason);
        }
    }
}
