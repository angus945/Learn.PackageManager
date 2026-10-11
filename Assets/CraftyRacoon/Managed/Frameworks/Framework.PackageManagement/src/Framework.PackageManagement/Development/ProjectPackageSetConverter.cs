using System;
using System.Collections.Generic;
using System.Threading;
using CraftyRacoon.PackageManagement.Development;

namespace CraftyRacoon.PackageManagement.Framework.Development
{
    public sealed class ProjectPackageSetConverter : IProjectPackageSetConverter
    {
        private readonly IUseExistingPackageSetEnvironment environment;

        public ProjectPackageSetConverter(IUseExistingPackageSetEnvironment environment)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public UseExistingPackageSetPreparation Prepare(UseExistingPackageSetRequest request, CancellationToken cancellation)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellation.ThrowIfCancellationRequested();

            ProjectWorkflowObservation projectObservation = environment.Observe(request);
            if (projectObservation == null) throw new InvalidOperationException("The Host did not return a Project observation.");
            cancellation.ThrowIfCancellationRequested();
            bool finalAuditStarted = false;

            try
            {
                IReadOnlyList<string> validationResult = environment.Validate(request, projectObservation);
                List<string> validationConflicts = new List<string>();
                PackageSetPlanValidation.AddValidationConflicts(validationResult, validationConflicts);
                PackageSetPlanValidation.ThrowIfAny(validationConflicts);
                cancellation.ThrowIfCancellationRequested();

                PackageSetResolution resolution = environment.AcquireOrResolve(request, cancellation);
                List<string> resolutionConflicts = ValidateResolution(request, resolution);
                PackageSetPlanValidation.ThrowIfAny(resolutionConflicts);
                cancellation.ThrowIfCancellationRequested();

                AuthoritativePackageSetLayout authoritativeLayout = environment.ReadAuthoritativeLayout(request, resolution);
                if (authoritativeLayout == null) throw new InvalidOperationException("The PackageSet authoritative layout is unavailable.");
                List<AuthoritativePackageSetEntry> authoritativeEntries = PackageSetPlanValidation.SortAuthoritativeEntries(authoritativeLayout.Entries);
                List<string> conflicts = new List<string>();
                List<PackagePlacement> placements = new List<PackagePlacement>();
                List<PackageSetEntryPlan> entries = ObserveEntries(request, resolution, authoritativeEntries, placements, conflicts, cancellation);
                PackageSetPlanValidation.CollectPlacementConflicts(authoritativeLayout.SourceRoot, authoritativeLayout.ProjectionRoot, placements, conflicts);
                PackageSetPlanValidation.CollectRuntimeConflicts(placements, conflicts);
                PackageSetModeObservation packageSetModeObservation = environment.ObservePackageSetMode(request, resolution, authoritativeLayout);
                bool projectBindingRequiresWrite = false;
                bool runtimeBindingsRequireWrite = false;
                if (packageSetModeObservation == null) conflicts.Add("The Host did not return a PackageSet-mode observation.");
                else CollectPackageSetModeConflicts(request, resolution, authoritativeLayout.ProjectionRoot, entries, packageSetModeObservation, conflicts, out projectBindingRequiresWrite, out runtimeBindingsRequireWrite);
                PackageSetPlanValidation.ThrowIfAny(conflicts);
                if (packageSetModeObservation == null) throw new InvalidOperationException("The PackageSet-mode observation was unavailable after validation.");

                PackageDirectoryLayout layout = new PackageDirectoryLayout(authoritativeLayout.SourceRoot, authoritativeLayout.ProjectionRoot, placements);
                PackageSourceDirectoryPolicy.RequireCanonicalLayout(layout);
                UseExistingPackageSetPlan plan = new UseExistingPackageSetPlan(request, projectObservation, resolution, layout, entries, packageSetModeObservation, projectBindingRequiresWrite, runtimeBindingsRequireWrite);
                cancellation.ThrowIfCancellationRequested();
                string applyInstructionsPath = string.Empty;
                if (!plan.AlreadyConfigured) applyInstructionsPath = environment.ExportOfflineApply(plan);
                VerifyPreparationEvidence(request, resolution, authoritativeLayout, authoritativeEntries, entries, packageSetModeObservation, cancellation);
                finalAuditStarted = true;
                ProjectWorkflowObservation postPrepareObservation = environment.Observe(request);
                PackageSetPlanValidation.RequireUnchangedProject(projectObservation, postPrepareObservation);
                if (!plan.AlreadyConfigured) environment.MarkOfflineApplyReady(applyInstructionsPath);
                return new UseExistingPackageSetPreparation(plan, applyInstructionsPath);
            }
            catch (Exception workflowError)
            {
                if (!finalAuditStarted) AuditFailedPreparation(request, projectObservation, workflowError);
                throw;
            }
        }

        private void AuditFailedPreparation(UseExistingPackageSetRequest request, ProjectWorkflowObservation projectObservation, Exception workflowError)
        {
            try
            {
                ProjectWorkflowObservation failedObservation = environment.Observe(request);
                PackageSetPlanValidation.RequireUnchangedProject(projectObservation, failedObservation);
            }
            catch (Exception auditError)
            {
                Exception[] errors = { workflowError, auditError };
                throw new AggregateException("Use Existing PackageSet failed and the Project preparation invariant also failed.", errors);
            }
        }

        private List<string> ValidateResolution(UseExistingPackageSetRequest request, PackageSetResolution resolution)
        {
            List<string> conflicts = new List<string>();
            if (resolution == null)
            {
                conflicts.Add("The PackageSet could not be acquired or resolved.");
                return conflicts;
            }
            if (!resolution.CheckoutPath.Equals(request.PackageSetCheckoutPath, StringComparison.Ordinal)) conflicts.Add("Resolved PackageSet checkout does not match the requested checkout path.");
            if (!string.IsNullOrWhiteSpace(request.PackageSetRepository) && !resolution.Repository.Equals(request.PackageSetRepository, StringComparison.Ordinal)) conflicts.Add("Resolved PackageSet repository does not match the requested repository identity.");
            if (!string.IsNullOrWhiteSpace(request.ExactRevision) && !resolution.ExactRevision.Equals(request.ExactRevision, StringComparison.OrdinalIgnoreCase)) conflicts.Add("Resolved PackageSet revision does not match the requested exact revision.");
            return conflicts;
        }

        private List<PackageSetEntryPlan> ObserveEntries(UseExistingPackageSetRequest request, PackageSetResolution resolution, IEnumerable<AuthoritativePackageSetEntry> authoritativeEntries, List<PackagePlacement> placements, List<string> conflicts, CancellationToken cancellation)
        {
            List<PackageSetEntryPlan> entries = new List<PackageSetEntryPlan>();
            foreach (AuthoritativePackageSetEntry authoritativeEntry in authoritativeEntries)
            {
                cancellation.ThrowIfCancellationRequested();
                ObservedPackageSetEntry observedEntry = environment.ObserveEntry(request, resolution, authoritativeEntry);
                if (observedEntry == null)
                {
                    conflicts.Add(authoritativeEntry.Identity + ": Project entry observation is unavailable.");
                    continue;
                }
                PackagePlacement placement = new PackagePlacement(authoritativeEntry.Identity, authoritativeEntry.SourcePath, observedEntry.ProjectSourcePath, authoritativeEntry.ProjectionPath, authoritativeEntry.Kind, authoritativeEntry.BootstrapRuntime);
                placements.Add(placement);
                DevelopmentBackingAssessment assessment = DevelopmentBackingPolicy.Evaluate(authoritativeEntry.Kind, observedEntry.Observation);
                PackageSetEntryPlan entry = new PackageSetEntryPlan(placement, observedEntry.Observation, assessment, authoritativeEntry.Repository, observedEntry.SourceRepository);
                entries.Add(entry);
                if (authoritativeEntry.Kind != DevelopmentSourceKind.GitSubmodule) conflicts.Add(authoritativeEntry.Identity + ": Use Existing PackageSet requires an exact GitSubmodule source; LocalDraft entries must be published first.");
                if (!observedEntry.SourceRepository.Equals(authoritativeEntry.Repository, StringComparison.Ordinal)) conflicts.Add(authoritativeEntry.Identity + ": observed source repository does not match the authoritative PackageSet submodule repository.");
                if (!observedEntry.Observation.ExactPin.Equals(authoritativeEntry.ExactRevision, StringComparison.OrdinalIgnoreCase)) conflicts.Add(authoritativeEntry.Identity + ": observed source pin does not match the authoritative PackageSet gitlink revision.");
                if (!assessment.CanProceed) conflicts.Add(authoritativeEntry.Identity + ": " + assessment.Reason);
            }
            return entries;
        }

        private void CollectPackageSetModeConflicts(UseExistingPackageSetRequest request, PackageSetResolution resolution, string projectionRoot, IReadOnlyList<PackageSetEntryPlan> entries, PackageSetModeObservation observation, List<string> conflicts, out bool projectBindingRequiresWrite, out bool runtimeBindingsRequireWrite)
        {
            projectBindingRequiresWrite = false;
            runtimeBindingsRequireWrite = false;
            CollectProjectBindingConflicts(request, resolution, projectionRoot, observation.ProjectBinding, conflicts, out projectBindingRequiresWrite);
            CollectRuntimeBindingConflicts(entries, observation.RuntimeBindings, conflicts, out runtimeBindingsRequireWrite);
        }

        private void CollectProjectBindingConflicts(UseExistingPackageSetRequest request, PackageSetResolution resolution, string projectionRoot, ObservedProjectPackageSetBinding observation, List<string> conflicts, out bool requiresWrite)
        {
            requiresWrite = false;
            if (observation.State == DurableBindingObservationState.Absent)
            {
                if (observation.TrackedInProjectIndex) conflicts.Add("Project PackageSet binding is absent from the worktree but still present in the Project Git index.");
                if (!observation.IndexMatchesWorktree) conflicts.Add("Project PackageSet binding absence is not aligned with the Project Git index.");
                requiresWrite = true;
                return;
            }
            if (observation.State == DurableBindingObservationState.Invalid)
            {
                AddObservationDiagnostics("Project PackageSet binding", observation.Diagnostics, conflicts);
                if (observation.Diagnostics.Count == 0) conflicts.Add("Project PackageSet binding is invalid.");
                if (!observation.IndexMatchesWorktree) conflicts.Add("Project PackageSet binding worktree content does not match the Project Git index.");
                return;
            }

            bool structurallyValid = true;
            if (observation.SchemaVersion != 3)
            {
                conflicts.Add("Project PackageSet binding must use schemaVersion 3.");
                structurallyValid = false;
            }
            if (!observation.TrackedInProjectIndex)
            {
                conflicts.Add("Project PackageSet binding must be tracked by the Project Git index before it can be treated as configured.");
                structurallyValid = false;
            }
            if (!observation.IndexMatchesWorktree)
            {
                conflicts.Add("Project PackageSet binding worktree content does not match the Project Git index.");
                structurallyValid = false;
            }
            if (!structurallyValid) return;

            if (request.AcquisitionStrategy == PackageSetAcquisitionStrategy.ResolveTrackingRefOnce)
            {
                conflicts.Add("The Project already has an exact PackageSet revision. ResolveTrackingRefOnce cannot be used again; select the recorded exact revision or local checkout.");
                return;
            }

            bool sameRepository = observation.Repository.Equals(resolution.Repository, StringComparison.Ordinal);
            bool sameTrackingRef = observation.TrackingRef.Equals(request.TrackingRef, StringComparison.Ordinal);
            bool sameRevision = observation.ResolvedRevision.Equals(resolution.ExactRevision, StringComparison.OrdinalIgnoreCase);
            bool sameProjectionRoot = observation.ProjectionRoot.Equals(projectionRoot, StringComparison.Ordinal);
            bool exactBinding = sameRepository && sameTrackingRef && sameRevision && sameProjectionRoot && !observation.NeedsPublish;
            requiresWrite = !exactBinding;
        }

        private void CollectRuntimeBindingConflicts(IReadOnlyList<PackageSetEntryPlan> entries, ObservedRuntimeBindings observation, List<string> conflicts, out bool requiresWrite)
        {
            requiresWrite = false;
            Dictionary<string, PackageSetEntryPlan> expectedEntries = ExpectedRuntimeEntries(entries);
            if (expectedEntries.Count == 0)
            {
                CollectZeroRuntimeBindingConflicts(observation, conflicts);
                return;
            }

            if (observation.State == DurableBindingObservationState.Absent)
            {
                if (observation.TrackedInProjectIndex) conflicts.Add("Runtime bindings are absent from the worktree but still present in the Project Git index.");
                if (!observation.IndexMatchesWorktree) conflicts.Add("Runtime binding absence is not aligned with the Project Git index.");
                requiresWrite = true;
                return;
            }
            if (observation.State == DurableBindingObservationState.Invalid)
            {
                AddObservationDiagnostics("Runtime bindings", observation.Diagnostics, conflicts);
                if (observation.Diagnostics.Count == 0) conflicts.Add("Runtime bindings are invalid.");
                if (!observation.IndexMatchesWorktree) conflicts.Add("Runtime binding worktree content does not match the Project Git index.");
                return;
            }

            bool structurallyValid = true;
            if (observation.SchemaVersion != 1)
            {
                conflicts.Add("Runtime bindings must use schemaVersion 1.");
                structurallyValid = false;
            }
            if (!observation.TrackedInProjectIndex)
            {
                conflicts.Add("Runtime bindings must be tracked by the Project Git index before they can be treated as configured.");
                structurallyValid = false;
            }
            if (!observation.IndexMatchesWorktree)
            {
                conflicts.Add("Runtime binding worktree content does not match the Project Git index.");
                structurallyValid = false;
            }
            if (observation.Entries.Count != expectedEntries.Count)
            {
                conflicts.Add("Runtime bindings must contain exactly the three canonical Package Manager runtime identities.");
                structurallyValid = false;
            }

            HashSet<string> observedIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObservedRuntimeBindingEntry observedEntry in observation.Entries)
            {
                if (string.IsNullOrWhiteSpace(observedEntry.Identity))
                {
                    conflicts.Add("Runtime binding contains an empty identity.");
                    structurallyValid = false;
                    continue;
                }
                if (!observedIdentities.Add(observedEntry.Identity))
                {
                    conflicts.Add("Runtime bindings contain a duplicate identity: " + observedEntry.Identity + ".");
                    structurallyValid = false;
                }
                if (!expectedEntries.ContainsKey(observedEntry.Identity))
                {
                    conflicts.Add("Runtime bindings contain an unexpected identity: " + observedEntry.Identity + ".");
                    structurallyValid = false;
                }
                if (string.IsNullOrWhiteSpace(observedEntry.Repository) || string.IsNullOrWhiteSpace(observedEntry.ProjectionPath))
                {
                    conflicts.Add(observedEntry.Identity + ": runtime binding repository and projection path are required.");
                    structurallyValid = false;
                }
            }
            foreach (KeyValuePair<string, PackageSetEntryPlan> expectedEntry in expectedEntries)
            {
                if (!observedIdentities.Contains(expectedEntry.Key))
                {
                    conflicts.Add("Runtime bindings are missing the canonical identity: " + expectedEntry.Key + ".");
                    structurallyValid = false;
                }
            }
            if (!structurallyValid) return;

            foreach (ObservedRuntimeBindingEntry observedEntry in observation.Entries)
            {
                PackageSetEntryPlan expectedEntry = expectedEntries[observedEntry.Identity];
                bool sameRepository = observedEntry.Repository.Equals(expectedEntry.SourceRepository, StringComparison.Ordinal);
                bool samePath = observedEntry.ProjectionPath.Equals(expectedEntry.Placement.ProjectionPath, StringComparison.Ordinal);
                if (!sameRepository || !samePath) requiresWrite = true;
            }
        }

        private void CollectZeroRuntimeBindingConflicts(ObservedRuntimeBindings observation, List<string> conflicts)
        {
            if (observation.State != DurableBindingObservationState.Absent)
            {
                conflicts.Add("Runtime bindings exist even though the PackageSet contains no Package Manager runtime entries.");
                AddObservationDiagnostics("Runtime bindings", observation.Diagnostics, conflicts);
                return;
            }
            if (observation.TrackedInProjectIndex) conflicts.Add("Runtime bindings are absent from the worktree but still present in the Project Git index.");
            if (!observation.IndexMatchesWorktree) conflicts.Add("Runtime binding absence is not aligned with the Project Git index.");
        }

        private Dictionary<string, PackageSetEntryPlan> ExpectedRuntimeEntries(IReadOnlyList<PackageSetEntryPlan> entries)
        {
            Dictionary<string, PackageSetEntryPlan> expectedEntries = new Dictionary<string, PackageSetEntryPlan>(StringComparer.Ordinal);
            foreach (PackageSetEntryPlan entry in entries)
            {
                if (entry.Placement.BootstrapRuntime) expectedEntries.Add(entry.Placement.Identity, entry);
            }
            return expectedEntries;
        }

        private void AddObservationDiagnostics(string prefix, IReadOnlyList<string> diagnostics, List<string> conflicts)
        {
            foreach (string diagnostic in diagnostics) conflicts.Add(prefix + ": " + diagnostic);
        }

        private void VerifyPreparationEvidence(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetLayout expectedLayout, IReadOnlyList<AuthoritativePackageSetEntry> expectedEntries, IReadOnlyList<PackageSetEntryPlan> expectedPlans, PackageSetModeObservation expectedMode, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            AuthoritativePackageSetLayout actualLayout = environment.ReadAuthoritativeLayout(request, resolution);
            if (actualLayout == null) throw new InvalidOperationException("PackageSet authority became unavailable after planning.");
            RequireSameLayout(expectedLayout, actualLayout);
            List<AuthoritativePackageSetEntry> actualEntries = PackageSetPlanValidation.SortAuthoritativeEntries(actualLayout.Entries);
            if (actualEntries.Count != expectedEntries.Count) throw new InvalidOperationException("PackageSet authority changed after planning.");
            for (int index = 0; index < expectedEntries.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                AuthoritativePackageSetEntry expectedEntry = expectedEntries[index];
                AuthoritativePackageSetEntry actualEntry = actualEntries[index];
                RequireSameAuthoritativeEntry(expectedEntry, actualEntry);
                ObservedPackageSetEntry actualObservation = environment.ObserveEntry(request, resolution, actualEntry);
                if (actualObservation == null) throw new InvalidOperationException(actualEntry.Identity + ": Project entry evidence became unavailable after planning.");
                RequireSameEntryObservation(expectedPlans[index], actualObservation);
            }
            PackageSetModeObservation actualMode = environment.ObservePackageSetMode(request, resolution, actualLayout);
            if (actualMode == null) throw new InvalidOperationException("PackageSet-mode evidence became unavailable after planning.");
            RequireSameModeObservation(expectedMode, actualMode);
        }

        private void RequireSameLayout(AuthoritativePackageSetLayout expected, AuthoritativePackageSetLayout actual)
        {
            bool sameSourceRoot = expected.SourceRoot.Equals(actual.SourceRoot, StringComparison.Ordinal);
            bool sameProjectionRoot = expected.ProjectionRoot.Equals(actual.ProjectionRoot, StringComparison.Ordinal);
            if (!sameSourceRoot || !sameProjectionRoot) throw new InvalidOperationException("PackageSet layout authority changed after planning.");
        }

        private void RequireSameAuthoritativeEntry(AuthoritativePackageSetEntry expected, AuthoritativePackageSetEntry actual)
        {
            bool sameIdentity = expected.Identity.Equals(actual.Identity, StringComparison.Ordinal);
            bool sameSourcePath = expected.SourcePath.Equals(actual.SourcePath, StringComparison.Ordinal);
            bool sameProjectionPath = expected.ProjectionPath.Equals(actual.ProjectionPath, StringComparison.Ordinal);
            bool sameRepository = expected.Repository.Equals(actual.Repository, StringComparison.Ordinal);
            bool sameRevision = expected.ExactRevision.Equals(actual.ExactRevision, StringComparison.OrdinalIgnoreCase);
            bool sameKind = expected.Kind == actual.Kind;
            bool sameRuntime = expected.BootstrapRuntime == actual.BootstrapRuntime;
            if (!sameIdentity || !sameSourcePath || !sameProjectionPath || !sameRepository || !sameRevision || !sameKind || !sameRuntime) throw new InvalidOperationException(expected.Identity + ": PackageSet entry authority changed after planning.");
        }

        private void RequireSameEntryObservation(PackageSetEntryPlan expected, ObservedPackageSetEntry actual)
        {
            DevelopmentSourceObservation before = expected.Observation;
            DevelopmentSourceObservation after = actual.Observation;
            bool sameProjectPath = expected.Placement.OriginalProjectPath.Equals(actual.ProjectSourcePath, StringComparison.Ordinal);
            bool sameRepository = expected.ObservedRepository.Equals(actual.SourceRepository, StringComparison.Ordinal);
            bool sameAvailability = before.SourceAvailable == after.SourceAvailable;
            bool sameSourceHead = before.SourceHead.Equals(after.SourceHead, StringComparison.OrdinalIgnoreCase);
            bool sameExactPin = before.ExactPin.Equals(after.ExactPin, StringComparison.OrdinalIgnoreCase);
            bool sameDirty = before.SourceDirty == after.SourceDirty;
            bool sameContent = before.ContentMatchesExactPin == after.ContentMatchesExactPin;
            bool sameProjectChanges = before.ProjectHasIndependentChanges == after.ProjectHasIndependentChanges;
            bool sameMetadata = before.MetadataOwnershipVerified == after.MetadataOwnershipVerified;
            bool samePathOwnership = before.PathOwnershipVerified == after.PathOwnershipVerified;
            bool sameIndex = before.IndexAlignmentVerified == after.IndexAlignmentVerified;
            bool sameState = before.ProjectionState == after.ProjectionState;
            bool sameEvidence = before.EvidenceToken.Equals(after.EvidenceToken, StringComparison.Ordinal);
            if (!sameProjectPath || !sameRepository || !sameAvailability || !sameSourceHead || !sameExactPin || !sameDirty || !sameContent || !sameProjectChanges || !sameMetadata || !samePathOwnership || !sameIndex || !sameState || !sameEvidence) throw new InvalidOperationException(expected.Placement.Identity + ": source or Project entry evidence changed after planning.");
        }

        private void RequireSameModeObservation(PackageSetModeObservation expected, PackageSetModeObservation actual)
        {
            RequireSameProjectBinding(expected.ProjectBinding, actual.ProjectBinding);
            RequireSameRuntimeBindings(expected.RuntimeBindings, actual.RuntimeBindings);
        }

        private void RequireSameProjectBinding(ObservedProjectPackageSetBinding expected, ObservedProjectPackageSetBinding actual)
        {
            bool sameState = expected.State == actual.State;
            bool sameSchema = expected.SchemaVersion == actual.SchemaVersion;
            bool sameRepository = expected.Repository.Equals(actual.Repository, StringComparison.Ordinal);
            bool sameTrackingRef = expected.TrackingRef.Equals(actual.TrackingRef, StringComparison.Ordinal);
            bool sameRevision = expected.ResolvedRevision.Equals(actual.ResolvedRevision, StringComparison.OrdinalIgnoreCase);
            bool sameProjectionRoot = expected.ProjectionRoot.Equals(actual.ProjectionRoot, StringComparison.Ordinal);
            bool samePublication = expected.NeedsPublish == actual.NeedsPublish;
            bool sameFingerprint = expected.ContentFingerprint.Equals(actual.ContentFingerprint, StringComparison.Ordinal);
            bool sameTracking = expected.TrackedInProjectIndex == actual.TrackedInProjectIndex;
            bool sameIndex = expected.IndexMatchesWorktree == actual.IndexMatchesWorktree;
            if (!sameState || !sameSchema || !sameRepository || !sameTrackingRef || !sameRevision || !sameProjectionRoot || !samePublication || !sameFingerprint || !sameTracking || !sameIndex) throw new InvalidOperationException("Project PackageSet binding changed after planning.");
        }

        private void RequireSameRuntimeBindings(ObservedRuntimeBindings expected, ObservedRuntimeBindings actual)
        {
            bool sameState = expected.State == actual.State;
            bool sameSchema = expected.SchemaVersion == actual.SchemaVersion;
            bool sameFingerprint = expected.ContentFingerprint.Equals(actual.ContentFingerprint, StringComparison.Ordinal);
            bool sameTracking = expected.TrackedInProjectIndex == actual.TrackedInProjectIndex;
            bool sameIndex = expected.IndexMatchesWorktree == actual.IndexMatchesWorktree;
            if (!sameState || !sameSchema || !sameFingerprint || !sameTracking || !sameIndex || expected.Entries.Count != actual.Entries.Count) throw new InvalidOperationException("Runtime binding evidence changed after planning.");
            for (int index = 0; index < expected.Entries.Count; index++)
            {
                ObservedRuntimeBindingEntry expectedEntry = expected.Entries[index];
                ObservedRuntimeBindingEntry actualEntry = actual.Entries[index];
                bool sameIdentity = expectedEntry.Identity.Equals(actualEntry.Identity, StringComparison.Ordinal);
                bool sameRepository = expectedEntry.Repository.Equals(actualEntry.Repository, StringComparison.Ordinal);
                bool samePath = expectedEntry.ProjectionPath.Equals(actualEntry.ProjectionPath, StringComparison.Ordinal);
                if (!sameIdentity || !sameRepository || !samePath) throw new InvalidOperationException("Runtime binding evidence changed after planning.");
            }
        }
    }
}
