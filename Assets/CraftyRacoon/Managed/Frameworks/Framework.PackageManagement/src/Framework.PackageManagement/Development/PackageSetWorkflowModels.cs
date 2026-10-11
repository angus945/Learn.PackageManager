using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using CraftyRacoon.PackageManagement.Development;

namespace CraftyRacoon.PackageManagement.Framework.Development
{
    public enum PackageSetAcquisitionStrategy { OpenLocalCheckout, CloneOrOpenExactRevision, ResolveTrackingRefOnce }

    internal static class ExactGitObjectId
    {
        internal static bool IsValid(string revision)
        {
            if (string.IsNullOrWhiteSpace(revision)) return false;
            bool supportedLength = revision.Length == 40 || revision.Length == 64;
            if (!supportedLength) return false;
            bool hasNonZeroCharacter = false;
            foreach (char character in revision)
            {
                if (!Uri.IsHexDigit(character)) return false;
                if (character != '0') hasNonZeroCharacter = true;
            }
            return hasNonZeroCharacter;
        }

        internal static void Require(string revision, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(revision)) throw new ArgumentException("Exact Git object id is required.", parameterName);
            if (!IsValid(revision)) throw new ArgumentException("Exact Git object id must contain 40 or 64 hexadecimal characters.", parameterName);
        }
    }

    public sealed class ProjectWorkflowObservation
    {
        public ProjectWorkflowObservation(string projectHead, string indexFingerprint, string evidenceToken)
        {
            ExactGitObjectId.Require(projectHead, nameof(projectHead));
            ExactGitObjectId.Require(indexFingerprint, nameof(indexFingerprint));
            if (string.IsNullOrWhiteSpace(evidenceToken)) throw new ArgumentException("Project evidence token is required.", nameof(evidenceToken));
            ProjectHead = projectHead;
            IndexFingerprint = indexFingerprint;
            EvidenceToken = evidenceToken;
        }

        public string ProjectHead { get; }
        public string IndexFingerprint { get; }
        public string EvidenceToken { get; }
    }

    public enum DurableBindingObservationState { Absent, Valid, Invalid }

    public sealed class ObservedProjectPackageSetBinding
    {
        public ObservedProjectPackageSetBinding(DurableBindingObservationState state, int schemaVersion, string repository, string trackingRef, string resolvedRevision, string projectionRoot, bool needsPublish, string contentFingerprint, bool trackedInProjectIndex, bool indexMatchesWorktree, IEnumerable<string> diagnostics)
        {
            if (!Enum.IsDefined(typeof(DurableBindingObservationState), state)) throw new ArgumentOutOfRangeException(nameof(state));
            State = state;
            SchemaVersion = schemaVersion;
            Repository = repository ?? string.Empty;
            TrackingRef = trackingRef ?? string.Empty;
            ResolvedRevision = resolvedRevision ?? string.Empty;
            ProjectionRoot = projectionRoot ?? string.Empty;
            NeedsPublish = needsPublish;
            ContentFingerprint = DurableBindingFingerprint.Require(state, contentFingerprint, nameof(contentFingerprint));
            TrackedInProjectIndex = trackedInProjectIndex;
            IndexMatchesWorktree = indexMatchesWorktree;
            Diagnostics = DurableBindingDiagnostics.Copy(diagnostics);
        }

        public DurableBindingObservationState State { get; }
        public int SchemaVersion { get; }
        public string Repository { get; }
        public string TrackingRef { get; }
        public string ResolvedRevision { get; }
        public string ProjectionRoot { get; }
        public bool NeedsPublish { get; }
        public string ContentFingerprint { get; }
        public bool TrackedInProjectIndex { get; }
        public bool IndexMatchesWorktree { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }

    public sealed class ObservedRuntimeBindingEntry
    {
        public ObservedRuntimeBindingEntry(string identity, string repository, string projectionPath)
        {
            Identity = identity ?? string.Empty;
            Repository = repository ?? string.Empty;
            ProjectionPath = projectionPath ?? string.Empty;
        }

        public string Identity { get; }
        public string Repository { get; }
        public string ProjectionPath { get; }
    }

    public sealed class ObservedRuntimeBindings
    {
        public ObservedRuntimeBindings(DurableBindingObservationState state, int schemaVersion, IEnumerable<ObservedRuntimeBindingEntry> entries, string contentFingerprint, bool trackedInProjectIndex, bool indexMatchesWorktree, IEnumerable<string> diagnostics)
        {
            if (!Enum.IsDefined(typeof(DurableBindingObservationState), state)) throw new ArgumentOutOfRangeException(nameof(state));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            State = state;
            SchemaVersion = schemaVersion;
            List<ObservedRuntimeBindingEntry> copiedEntries = new List<ObservedRuntimeBindingEntry>();
            foreach (ObservedRuntimeBindingEntry entry in entries)
            {
                if (entry == null) throw new ArgumentException("Observed runtime binding entry cannot be null.", nameof(entries));
                copiedEntries.Add(entry);
            }
            copiedEntries.Sort(ObservedRuntimeBindingEntryComparer.Instance);
            Entries = new ReadOnlyCollection<ObservedRuntimeBindingEntry>(copiedEntries.ToArray());
            ContentFingerprint = DurableBindingFingerprint.Require(state, contentFingerprint, nameof(contentFingerprint));
            TrackedInProjectIndex = trackedInProjectIndex;
            IndexMatchesWorktree = indexMatchesWorktree;
            Diagnostics = DurableBindingDiagnostics.Copy(diagnostics);
        }

        public DurableBindingObservationState State { get; }
        public int SchemaVersion { get; }
        public IReadOnlyList<ObservedRuntimeBindingEntry> Entries { get; }
        public string ContentFingerprint { get; }
        public bool TrackedInProjectIndex { get; }
        public bool IndexMatchesWorktree { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        private sealed class ObservedRuntimeBindingEntryComparer : IComparer<ObservedRuntimeBindingEntry>
        {
            internal static readonly ObservedRuntimeBindingEntryComparer Instance = new ObservedRuntimeBindingEntryComparer();

            public int Compare(ObservedRuntimeBindingEntry first, ObservedRuntimeBindingEntry second)
            {
                return StringComparer.Ordinal.Compare(first.Identity, second.Identity);
            }
        }
    }

    public sealed class PackageSetModeObservation
    {
        public PackageSetModeObservation(ObservedProjectPackageSetBinding projectBinding, ObservedRuntimeBindings runtimeBindings)
        {
            ProjectBinding = projectBinding ?? throw new ArgumentNullException(nameof(projectBinding));
            RuntimeBindings = runtimeBindings ?? throw new ArgumentNullException(nameof(runtimeBindings));
        }

        public ObservedProjectPackageSetBinding ProjectBinding { get; }
        public ObservedRuntimeBindings RuntimeBindings { get; }
    }

    internal static class DurableBindingFingerprint
    {
        internal const string Absent = "missing";

        internal static string Require(DurableBindingObservationState state, string fingerprint, string parameterName)
        {
            string value = fingerprint ?? string.Empty;
            if (state == DurableBindingObservationState.Absent || value.Equals(Absent, StringComparison.Ordinal))
            {
                if (!value.Equals(Absent, StringComparison.Ordinal)) throw new ArgumentException("Absent durable binding fingerprint must be the missing sentinel.", parameterName);
                if (state == DurableBindingObservationState.Valid) throw new ArgumentException("A valid durable binding must have a present content fingerprint.", parameterName);
                return value;
            }
            if (value.Length != 64) throw new ArgumentException("Present durable binding fingerprint must be a SHA-256 value.", parameterName);
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character)) throw new ArgumentException("Present durable binding fingerprint must be a SHA-256 value.", parameterName);
            }
            return value.ToLowerInvariant();
        }
    }

    internal static class DurableBindingDiagnostics
    {
        internal static ReadOnlyCollection<string> Copy(IEnumerable<string> diagnostics)
        {
            List<string> copiedDiagnostics = new List<string>();
            if (diagnostics != null)
            {
                foreach (string diagnostic in diagnostics)
                {
                    if (!string.IsNullOrWhiteSpace(diagnostic)) copiedDiagnostics.Add(diagnostic);
                }
            }
            copiedDiagnostics.Sort(StringComparer.Ordinal);
            string[] diagnosticArray = copiedDiagnostics.ToArray();
            return new ReadOnlyCollection<string>(diagnosticArray);
        }
    }

    public sealed class CreatePackageSetSelection
    {
        public CreatePackageSetSelection(string identity, string projectSourcePath, DevelopmentSourceKind kind, bool bootstrapRuntime = false)
        {
            Identity = PackageRelativePath.Normalize(identity);
            if (Identity.Contains("/")) throw new ArgumentException("Package identity must not be a path.", nameof(identity));
            ProjectSourcePath = PackageRelativePath.Normalize(projectSourcePath);
            if (!Enum.IsDefined(typeof(DevelopmentSourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            BootstrapRuntime = bootstrapRuntime;
        }

        public string Identity { get; }
        public string ProjectSourcePath { get; }
        public DevelopmentSourceKind Kind { get; }
        public bool BootstrapRuntime { get; }
    }

    public sealed class CreatePackageSetRequest
    {
        public CreatePackageSetRequest(string projectRoot, string packageSetRoot, string packageSetIdentity, string packageSetRepository, string trackingRef, string sourceRoot, string projectionRoot, IEnumerable<CreatePackageSetSelection> selections)
        {
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root is required.", nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(packageSetRoot)) throw new ArgumentException("New PackageSet root is required.", nameof(packageSetRoot));
            ProjectRoot = projectRoot;
            PackageSetRoot = packageSetRoot;
            PackageSetIdentity = PackageRelativePath.Normalize(packageSetIdentity);
            if (PackageSetIdentity.Contains("/")) throw new ArgumentException("PackageSet identity must not be a path.", nameof(packageSetIdentity));
            PackageSetRepository = packageSetRepository ?? string.Empty;
            TrackingRef = string.IsNullOrWhiteSpace(trackingRef) ? "main" : trackingRef;
            SourceRoot = PackageRelativePath.Normalize(sourceRoot);
            ProjectionRoot = PackageRelativePath.Normalize(projectionRoot);
            if (selections == null) throw new ArgumentNullException(nameof(selections));
            List<CreatePackageSetSelection> copiedSelections = new List<CreatePackageSetSelection>();
            foreach (CreatePackageSetSelection selection in selections)
            {
                if (selection == null) throw new ArgumentException("Create selection cannot be null.", nameof(selections));
                copiedSelections.Add(selection);
            }
            Selections = new ReadOnlyCollection<CreatePackageSetSelection>(copiedSelections.ToArray());
        }

        public string ProjectRoot { get; }
        public string PackageSetRoot { get; }
        public string PackageSetIdentity { get; }
        public string PackageSetRepository { get; }
        public string TrackingRef { get; }
        public string SourceRoot { get; }
        public string ProjectionRoot { get; }
        public IReadOnlyList<CreatePackageSetSelection> Selections { get; }
    }

    public sealed class UseExistingPackageSetRequest
    {
        public UseExistingPackageSetRequest(string projectRoot, string packageSetCheckoutPath, string packageSetRepository, string trackingRef, string exactRevision, PackageSetAcquisitionStrategy acquisitionStrategy)
        {
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root is required.", nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(packageSetCheckoutPath)) throw new ArgumentException("PackageSet checkout path is required.", nameof(packageSetCheckoutPath));
            if (!Enum.IsDefined(typeof(PackageSetAcquisitionStrategy), acquisitionStrategy)) throw new ArgumentOutOfRangeException(nameof(acquisitionStrategy));
            ProjectRoot = projectRoot;
            PackageSetCheckoutPath = packageSetCheckoutPath;
            PackageSetRepository = packageSetRepository ?? string.Empty;
            TrackingRef = string.IsNullOrWhiteSpace(trackingRef) ? "main" : trackingRef;
            ExactRevision = exactRevision ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(ExactRevision)) ExactGitObjectId.Require(ExactRevision, nameof(exactRevision));
            AcquisitionStrategy = acquisitionStrategy;
            ValidateAcquisitionInput();
        }

        public string ProjectRoot { get; }
        public string PackageSetCheckoutPath { get; }
        public string PackageSetRepository { get; }
        public string TrackingRef { get; }
        public string ExactRevision { get; }
        public PackageSetAcquisitionStrategy AcquisitionStrategy { get; }

        private void ValidateAcquisitionInput()
        {
            bool requiresRepository = AcquisitionStrategy != PackageSetAcquisitionStrategy.OpenLocalCheckout;
            if (requiresRepository && string.IsNullOrWhiteSpace(PackageSetRepository)) throw new ArgumentException("The selected acquisition strategy requires a PackageSet repository.", nameof(PackageSetRepository));
            bool requiresExactRevision = AcquisitionStrategy == PackageSetAcquisitionStrategy.CloneOrOpenExactRevision;
            if (requiresExactRevision && string.IsNullOrWhiteSpace(ExactRevision)) throw new ArgumentException("Exact-revision acquisition requires an immutable PackageSet revision.", nameof(ExactRevision));
            bool resolvesTrackingRef = AcquisitionStrategy == PackageSetAcquisitionStrategy.ResolveTrackingRefOnce;
            if (resolvesTrackingRef && !string.IsNullOrWhiteSpace(ExactRevision)) throw new ArgumentException("ResolveTrackingRefOnce is only valid before an exact PackageSet revision exists.", nameof(ExactRevision));
        }
    }

    public sealed class PackageSetWorkspace
    {
        public PackageSetWorkspace(string checkoutPath)
        {
            if (string.IsNullOrWhiteSpace(checkoutPath)) throw new ArgumentException("PackageSet checkout path is required.", nameof(checkoutPath));
            CheckoutPath = checkoutPath;
        }

        public string CheckoutPath { get; }
    }

    public sealed class PackageSetResolution
    {
        public PackageSetResolution(string checkoutPath, string repository, string exactRevision)
        {
            if (string.IsNullOrWhiteSpace(checkoutPath)) throw new ArgumentException("Resolved PackageSet checkout path is required.", nameof(checkoutPath));
            ExactGitObjectId.Require(exactRevision, nameof(exactRevision));
            CheckoutPath = checkoutPath;
            Repository = repository ?? string.Empty;
            ExactRevision = exactRevision;
        }

        public string CheckoutPath { get; }
        public string Repository { get; }
        public string ExactRevision { get; }
    }

    public sealed class AuthoritativePackageSetEntry
    {
        public AuthoritativePackageSetEntry(string identity, string sourcePath, string projectionPath, string repository, string exactRevision, DevelopmentSourceKind kind, bool bootstrapRuntime = false)
        {
            Identity = PackageRelativePath.Normalize(identity);
            if (Identity.Contains("/")) throw new ArgumentException("Package identity must not be a path.", nameof(identity));
            SourcePath = PackageRelativePath.Normalize(sourcePath);
            ProjectionPath = PackageRelativePath.Normalize(projectionPath);
            if (string.IsNullOrWhiteSpace(repository)) throw new ArgumentException("Authoritative source repository is required.", nameof(repository));
            Repository = repository;
            ExactGitObjectId.Require(exactRevision, nameof(exactRevision));
            ExactRevision = exactRevision;
            if (!Enum.IsDefined(typeof(DevelopmentSourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            BootstrapRuntime = bootstrapRuntime;
        }

        public string Identity { get; }
        public string SourcePath { get; }
        public string ProjectionPath { get; }
        public string Repository { get; }
        public string ExactRevision { get; }
        public DevelopmentSourceKind Kind { get; }
        public bool BootstrapRuntime { get; }
    }

    public sealed class AuthoritativePackageSetLayout
    {
        public AuthoritativePackageSetLayout(string sourceRoot, string projectionRoot, IEnumerable<AuthoritativePackageSetEntry> entries)
        {
            SourceRoot = PackageRelativePath.Normalize(sourceRoot);
            ProjectionRoot = PackageRelativePath.Normalize(projectionRoot);
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            List<AuthoritativePackageSetEntry> copiedEntries = new List<AuthoritativePackageSetEntry>();
            foreach (AuthoritativePackageSetEntry entry in entries)
            {
                if (entry == null) throw new ArgumentException("Authoritative PackageSet entry cannot be null.", nameof(entries));
                copiedEntries.Add(entry);
            }
            Entries = new ReadOnlyCollection<AuthoritativePackageSetEntry>(copiedEntries.ToArray());
        }

        public string SourceRoot { get; }
        public string ProjectionRoot { get; }
        public IReadOnlyList<AuthoritativePackageSetEntry> Entries { get; }
    }

    public sealed class ObservedPackageSetEntry
    {
        public ObservedPackageSetEntry(string projectSourcePath, string sourceRepository, DevelopmentSourceObservation observation)
        {
            ProjectSourcePath = PackageRelativePath.Normalize(projectSourcePath);
            if (string.IsNullOrWhiteSpace(sourceRepository)) throw new ArgumentException("Observed source repository is required.", nameof(sourceRepository));
            SourceRepository = sourceRepository;
            Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        }

        public string ProjectSourcePath { get; }
        public string SourceRepository { get; }
        public DevelopmentSourceObservation Observation { get; }
    }

    public sealed class ObservedCreatePackageSetSource
    {
        public ObservedCreatePackageSetSource(string sourceRepository, DevelopmentSourceObservation observation)
        {
            SourceRepository = sourceRepository ?? string.Empty;
            Observation = observation ?? throw new ArgumentNullException(nameof(observation));
        }

        public string SourceRepository { get; }
        public DevelopmentSourceObservation Observation { get; }
    }

    public sealed class PackageSetEntryPlan
    {
        internal PackageSetEntryPlan(PackagePlacement placement, DevelopmentSourceObservation observation, DevelopmentBackingAssessment assessment, string sourceRepository, string observedRepository)
        {
            Placement = placement;
            Observation = observation;
            Assessment = assessment;
            SourceRepository = sourceRepository ?? string.Empty;
            ObservedRepository = observedRepository ?? string.Empty;
        }

        public PackagePlacement Placement { get; }
        public DevelopmentSourceObservation Observation { get; }
        public DevelopmentBackingAssessment Assessment { get; }
        public string SourceRepository { get; }
        public string ObservedRepository { get; }
    }

    public sealed class CreatePackageSetPlan
    {
        internal CreatePackageSetPlan(CreatePackageSetRequest request, ProjectWorkflowObservation projectObservation, PackageSetWorkspace workspace, PackageDirectoryLayout layout, IEnumerable<PackageSetEntryPlan> entries)
        {
            Request = request;
            ProjectObservation = projectObservation ?? throw new ArgumentNullException(nameof(projectObservation));
            Workspace = workspace;
            Layout = layout;
            List<PackageSetEntryPlan> copiedEntries = CopyEntries(entries);
            Entries = new ReadOnlyCollection<PackageSetEntryPlan>(copiedEntries.ToArray());
            NeedsPublish = string.IsNullOrWhiteSpace(request.PackageSetRepository) || HasUnpublishedEntry(copiedEntries);
        }

        public CreatePackageSetRequest Request { get; }
        public ProjectWorkflowObservation ProjectObservation { get; }
        public PackageSetWorkspace Workspace { get; }
        public PackageDirectoryLayout Layout { get; }
        public IReadOnlyList<PackageSetEntryPlan> Entries { get; }
        public bool NeedsPublish { get; }

        private static List<PackageSetEntryPlan> CopyEntries(IEnumerable<PackageSetEntryPlan> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            List<PackageSetEntryPlan> copiedEntries = new List<PackageSetEntryPlan>();
            foreach (PackageSetEntryPlan entry in entries) copiedEntries.Add(entry);
            return copiedEntries;
        }

        private static bool HasUnpublishedEntry(IEnumerable<PackageSetEntryPlan> entries)
        {
            foreach (PackageSetEntryPlan entry in entries)
            {
                if (entry.Assessment.NeedsPublish) return true;
            }
            return false;
        }
    }

    public sealed class UseExistingPackageSetPlan
    {
        internal UseExistingPackageSetPlan(UseExistingPackageSetRequest request, ProjectWorkflowObservation projectObservation, PackageSetResolution resolution, PackageDirectoryLayout layout, IEnumerable<PackageSetEntryPlan> entries, PackageSetModeObservation packageSetModeObservation, bool projectBindingRequiresWrite, bool runtimeBindingsRequireWrite)
        {
            Request = request;
            ProjectObservation = projectObservation ?? throw new ArgumentNullException(nameof(projectObservation));
            Resolution = resolution;
            Layout = layout;
            List<PackageSetEntryPlan> copiedEntries = new List<PackageSetEntryPlan>();
            bool allConfigured = true;
            foreach (PackageSetEntryPlan entry in entries)
            {
                copiedEntries.Add(entry);
                if (entry.Assessment.Decision != DevelopmentBackingDecision.AlreadyConfigured) allConfigured = false;
            }
            Entries = new ReadOnlyCollection<PackageSetEntryPlan>(copiedEntries.ToArray());
            PackageSetModeObservation = packageSetModeObservation ?? throw new ArgumentNullException(nameof(packageSetModeObservation));
            ProjectBindingRequiresWrite = projectBindingRequiresWrite;
            RuntimeBindingsRequireWrite = runtimeBindingsRequireWrite;
            AlreadyConfigured = copiedEntries.Count != 0 && allConfigured && !projectBindingRequiresWrite && !runtimeBindingsRequireWrite;
        }

        public UseExistingPackageSetRequest Request { get; }
        public ProjectWorkflowObservation ProjectObservation { get; }
        public PackageSetResolution Resolution { get; }
        public PackageDirectoryLayout Layout { get; }
        public IReadOnlyList<PackageSetEntryPlan> Entries { get; }
        public PackageSetModeObservation PackageSetModeObservation { get; }
        public bool ProjectBindingRequiresWrite { get; }
        public bool RuntimeBindingsRequireWrite { get; }
        public bool AlreadyConfigured { get; }
    }

    public sealed class CreatePackageSetPreparation
    {
        internal CreatePackageSetPreparation(CreatePackageSetPlan plan, PackageSetResolution resolution, string applyInstructionsPath)
        {
            Plan = plan;
            Resolution = resolution;
            ApplyInstructionsPath = applyInstructionsPath ?? string.Empty;
        }

        public CreatePackageSetPlan Plan { get; }
        public PackageSetResolution Resolution { get; }
        public string ApplyInstructionsPath { get; }
    }

    public sealed class UseExistingPackageSetPreparation
    {
        internal UseExistingPackageSetPreparation(UseExistingPackageSetPlan plan, string applyInstructionsPath)
        {
            Plan = plan;
            ApplyInstructionsPath = applyInstructionsPath ?? string.Empty;
        }

        public UseExistingPackageSetPlan Plan { get; }
        public string ApplyInstructionsPath { get; }
    }

    public sealed class PackageSetWorkflowConflictException : InvalidOperationException
    {
        internal PackageSetWorkflowConflictException(IEnumerable<string> conflicts) : base(BuildMessage(conflicts, out ReadOnlyCollection<string> copiedConflicts))
        {
            Conflicts = copiedConflicts;
        }

        public IReadOnlyList<string> Conflicts { get; }

        private static string BuildMessage(IEnumerable<string> conflicts, out ReadOnlyCollection<string> copiedConflicts)
        {
            if (conflicts == null) throw new ArgumentNullException(nameof(conflicts));
            List<string> messages = new List<string>();
            foreach (string conflict in conflicts)
            {
                if (!string.IsNullOrWhiteSpace(conflict)) messages.Add(conflict);
            }
            messages.Sort(StringComparer.Ordinal);
            string[] messageArray = messages.ToArray();
            copiedConflicts = new ReadOnlyCollection<string>(messageArray);
            return string.Join(Environment.NewLine, messageArray);
        }
    }

    public interface IPackageSetCreator
    {
        CreatePackageSetPreparation Prepare(CreatePackageSetRequest request, CancellationToken cancellation);
    }

    public interface IProjectPackageSetConverter
    {
        UseExistingPackageSetPreparation Prepare(UseExistingPackageSetRequest request, CancellationToken cancellation);
    }

    public interface ICreatePackageSetEnvironment
    {
        ProjectWorkflowObservation Observe(CreatePackageSetRequest request);
        IReadOnlyList<string> Validate(CreatePackageSetRequest request, ProjectWorkflowObservation observation);
        PackageSetWorkspace Acquire(CreatePackageSetRequest request, CancellationToken cancellation);
        ObservedCreatePackageSetSource ObserveSelection(CreatePackageSetRequest request, PackageSetWorkspace workspace, PackagePlacement placement);
        PackageSetResolution PreparePackageSet(CreatePackageSetPlan plan, CancellationToken cancellation);
        string ExportOfflineApply(CreatePackageSetPlan plan, PackageSetResolution resolution);
        void MarkOfflineApplyReady(string applyInstructionsPath);
    }

    public interface IUseExistingPackageSetEnvironment
    {
        ProjectWorkflowObservation Observe(UseExistingPackageSetRequest request);
        IReadOnlyList<string> Validate(UseExistingPackageSetRequest request, ProjectWorkflowObservation observation);
        PackageSetResolution AcquireOrResolve(UseExistingPackageSetRequest request, CancellationToken cancellation);
        AuthoritativePackageSetLayout ReadAuthoritativeLayout(UseExistingPackageSetRequest request, PackageSetResolution resolution);
        ObservedPackageSetEntry ObserveEntry(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetEntry entry);
        PackageSetModeObservation ObservePackageSetMode(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetLayout authoritativeLayout);
        string ExportOfflineApply(UseExistingPackageSetPlan plan);
        void MarkOfflineApplyReady(string applyInstructionsPath);
    }
}
