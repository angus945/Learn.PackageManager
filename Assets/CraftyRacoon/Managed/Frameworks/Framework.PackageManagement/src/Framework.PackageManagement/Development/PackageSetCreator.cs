using System;
using System.Collections.Generic;
using System.Threading;
using CraftyRacoon.PackageManagement.Development;

namespace CraftyRacoon.PackageManagement.Framework.Development
{
    public sealed class PackageSetCreator : IPackageSetCreator
    {
        private readonly ICreatePackageSetEnvironment environment;

        public PackageSetCreator(ICreatePackageSetEnvironment environment)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public CreatePackageSetPreparation Prepare(CreatePackageSetRequest request, CancellationToken cancellation)
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

                PackageSetWorkspace workspace = environment.Acquire(request, cancellation);
                if (workspace == null) throw new InvalidOperationException("The Host did not return a new PackageSet workspace.");
                if (!workspace.CheckoutPath.Equals(request.PackageSetRoot, StringComparison.Ordinal)) throw new InvalidOperationException("The acquired PackageSet workspace does not match the requested destination.");
                cancellation.ThrowIfCancellationRequested();

                List<CreatePackageSetSelection> selections = PackageSetPlanValidation.SortSelections(request.Selections);
                List<PackagePlacement> placements = CreatePlacements(request, selections);
                List<string> conflicts = new List<string>();
                PackageSetPlanValidation.CollectPlacementConflicts(request.SourceRoot, request.ProjectionRoot, placements, conflicts);
                PackageSetPlanValidation.CollectRuntimeConflicts(placements, conflicts);
                List<PackageSetEntryPlan> entries = ObserveSelections(request, workspace, placements, conflicts, cancellation);
                PackageSetPlanValidation.ThrowIfAny(conflicts);

                PackageDirectoryLayout layout = new PackageDirectoryLayout(request.SourceRoot, request.ProjectionRoot, placements);
                PackageSourceDirectoryPolicy.RequireCanonicalLayout(layout);
                CreatePackageSetPlan plan = new CreatePackageSetPlan(request, projectObservation, workspace, layout, entries);
                cancellation.ThrowIfCancellationRequested();

                PackageSetResolution resolution = environment.PreparePackageSet(plan, cancellation);
                ValidatePreparedResolution(request, workspace, resolution);
                cancellation.ThrowIfCancellationRequested();
                string applyInstructionsPath = environment.ExportOfflineApply(plan, resolution);
                finalAuditStarted = true;
                ProjectWorkflowObservation postPrepareObservation = environment.Observe(request);
                PackageSetPlanValidation.RequireUnchangedProject(projectObservation, postPrepareObservation);
                environment.MarkOfflineApplyReady(applyInstructionsPath);
                return new CreatePackageSetPreparation(plan, resolution, applyInstructionsPath);
            }
            catch (Exception workflowError)
            {
                if (!finalAuditStarted) AuditFailedPreparation(request, projectObservation, workflowError);
                throw;
            }
        }

        private void AuditFailedPreparation(CreatePackageSetRequest request, ProjectWorkflowObservation projectObservation, Exception workflowError)
        {
            try
            {
                ProjectWorkflowObservation failedObservation = environment.Observe(request);
                PackageSetPlanValidation.RequireUnchangedProject(projectObservation, failedObservation);
            }
            catch (Exception auditError)
            {
                Exception[] errors = { workflowError, auditError };
                throw new AggregateException("PackageSet creation failed and the Project preparation invariant also failed.", errors);
            }
        }

        private List<PackagePlacement> CreatePlacements(CreatePackageSetRequest request, IEnumerable<CreatePackageSetSelection> selections)
        {
            List<PackagePlacement> placements = new List<PackagePlacement>();
            foreach (CreatePackageSetSelection selection in selections)
            {
                string sourcePath = PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath(request.SourceRoot, selection.Identity);
                string projectionPath = PackageSourceDirectoryPolicy.ResolveManagedProjectionPath(request.ProjectionRoot, selection.Identity);
                PackagePlacement placement = new PackagePlacement(selection.Identity, sourcePath, selection.ProjectSourcePath, projectionPath, selection.Kind, selection.BootstrapRuntime);
                placements.Add(placement);
            }
            return placements;
        }

        private List<PackageSetEntryPlan> ObserveSelections(CreatePackageSetRequest request, PackageSetWorkspace workspace, IEnumerable<PackagePlacement> placements, List<string> conflicts, CancellationToken cancellation)
        {
            List<PackageSetEntryPlan> entries = new List<PackageSetEntryPlan>();
            foreach (PackagePlacement placement in placements)
            {
                cancellation.ThrowIfCancellationRequested();
                ObservedCreatePackageSetSource observedSource = environment.ObserveSelection(request, workspace, placement);
                if (observedSource == null)
                {
                    conflicts.Add(placement.Identity + ": source observation is unavailable.");
                    continue;
                }
                DevelopmentSourceObservation observation = observedSource.Observation;
                CollectSourceAuthorityConflicts(placement, observedSource, conflicts);
                DevelopmentBackingAssessment assessment = DevelopmentBackingPolicy.Evaluate(placement.Kind, observation);
                PackageSetEntryPlan entry = new PackageSetEntryPlan(placement, observation, assessment, observedSource.SourceRepository, observedSource.SourceRepository);
                entries.Add(entry);
                if (!assessment.CanProceed) conflicts.Add(placement.Identity + ": " + assessment.Reason);
            }
            return entries;
        }

        private static void CollectSourceAuthorityConflicts(PackagePlacement placement, ObservedCreatePackageSetSource observedSource, List<string> conflicts)
        {
            DevelopmentSourceObservation observation = observedSource.Observation;
            if (!ExactGitObjectId.IsValid(observation.SourceHead)) conflicts.Add(placement.Identity + ": observed source HEAD is not an exact Git object id.");
            if (placement.Kind != DevelopmentSourceKind.GitSubmodule) return;
            if (string.IsNullOrWhiteSpace(observedSource.SourceRepository)) conflicts.Add(placement.Identity + ": GitSubmodule source repository identity is unavailable.");
            if (!ExactGitObjectId.IsValid(observation.ExactPin)) conflicts.Add(placement.Identity + ": GitSubmodule source pin is not an exact Git object id.");
        }

        private static void ValidatePreparedResolution(CreatePackageSetRequest request, PackageSetWorkspace workspace, PackageSetResolution resolution)
        {
            if (resolution == null) throw new InvalidOperationException("The Host did not return the committed PackageSet revision.");
            if (!resolution.CheckoutPath.Equals(workspace.CheckoutPath, StringComparison.Ordinal)) throw new InvalidOperationException("The prepared PackageSet revision belongs to another checkout.");
            if (!resolution.CheckoutPath.Equals(request.PackageSetRoot, StringComparison.Ordinal)) throw new InvalidOperationException("The prepared PackageSet checkout does not match the Create request.");
            if (!string.IsNullOrWhiteSpace(request.PackageSetRepository) && !resolution.Repository.Equals(request.PackageSetRepository, StringComparison.Ordinal)) throw new InvalidOperationException("The prepared PackageSet repository does not match the Create request.");
        }
    }
}
