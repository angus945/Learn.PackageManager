using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CraftyRacoon.PackageManagement.Development;
using CraftyRacoon.PackageManagement.Framework.Development;
using UnityEngine;
using IO = CraftyRacoon.PackageManagement.Unity3D.Editor.Development.DevelopmentBootstrapIO;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    internal sealed class WindowsUseExistingPackageSetEnvironment : IUseExistingPackageSetEnvironment
    {
        private sealed class ProjectSubmodule
        {
            internal string Section = string.Empty;
            internal string Path = string.Empty;
            internal string Repository = string.Empty;
        }

        [Serializable]
        private sealed class RawProjectPackageSetBinding
        {
            public int schemaVersion = -1;
            public string repository = string.Empty;
            public string trackingRef = string.Empty;
            public string resolvedRevision = string.Empty;
            public string managedProjectionRoot = string.Empty;
            public bool needsPublish = false;
        }

        [Serializable]
        private sealed class RawRuntimeBindings
        {
            public int schemaVersion = -1;
            public RawRuntimeBindingEntry[]? components = null;
        }

        [Serializable]
        private sealed class RawRuntimeBindingEntry
        {
            public string id = string.Empty;
            public string repository = string.Empty;
            public string path = string.Empty;
        }

        private readonly string helperPath;
        private readonly string unityExecutable;
        private string manifestFingerprint = string.Empty;
        private string gitmodulesFingerprint = string.Empty;
        private string gitlinksEvidence = string.Empty;
        private string authorityRevision = string.Empty;

        internal WindowsUseExistingPackageSetEnvironment(string helperPath, string unityExecutable)
        {
            this.helperPath = helperPath;
            this.unityExecutable = unityExecutable;
        }

        public ProjectWorkflowObservation Observe(UseExistingPackageSetRequest request)
        {
            string projectGitRoot;
            string projectGitDirectory;
            return WindowsPackageSetEnvironmentSupport.ObserveProject(request.ProjectRoot, out projectGitRoot, out projectGitDirectory);
        }

        public IReadOnlyList<string> Validate(UseExistingPackageSetRequest request, ProjectWorkflowObservation observation)
        {
            IReadOnlyList<string> commonConflicts = WindowsPackageSetEnvironmentSupport.ValidateCommon(request.ProjectRoot, request.PackageSetCheckoutPath, helperPath, false);
            List<string> conflicts = new List<string>(commonConflicts);
            if (request.AcquisitionStrategy == PackageSetAcquisitionStrategy.OpenLocalCheckout && !Directory.Exists(request.PackageSetCheckoutPath))
                conflicts.Add("The selected local PackageSet checkout does not exist.");
            if (!string.IsNullOrWhiteSpace(request.PackageSetRepository))
            {
                try
                {
                    IO.RequireRemote(request.PackageSetRepository);
                }
                catch (Exception error)
                {
                    conflicts.Add("PackageSet repository is invalid: " + error.Message);
                }
            }
            if (request.AcquisitionStrategy == PackageSetAcquisitionStrategy.ResolveTrackingRefOnce)
            {
                try
                {
                    bool fullTrackingRef = request.TrackingRef.StartsWith("refs/", StringComparison.Ordinal);
                    if (fullTrackingRef) IO.Git(request.ProjectRoot, "check-ref-format", request.TrackingRef);
                    else IO.Git(request.ProjectRoot, "check-ref-format", "--branch", request.TrackingRef);
                }
                catch (Exception error)
                {
                    conflicts.Add("PackageSet tracking ref is invalid: " + error.Message);
                }
            }
            return conflicts;
        }

        public PackageSetResolution AcquireOrResolve(UseExistingPackageSetRequest request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string checkout = IO.Full(request.PackageSetCheckoutPath);
            string exactRevision = request.ExactRevision;
            if (request.AcquisitionStrategy == PackageSetAcquisitionStrategy.ResolveTrackingRefOnce)
                exactRevision = IO.ResolveTrackingRef(request.ProjectRoot, request.PackageSetRepository, request.TrackingRef, cancellation);

            if (request.AcquisitionStrategy == PackageSetAcquisitionStrategy.OpenLocalCheckout)
            {
                ValidateIndependentCheckout(checkout);
                ValidateCleanPackageSet(checkout);
                ValidateRepositoryIdentity(checkout, request.PackageSetRepository);
                string localHead = IO.Git(checkout, "rev-parse", "HEAD");
                bool exactRevisionProvided = !string.IsNullOrWhiteSpace(exactRevision);
                bool localHeadMatches = localHead.Equals(exactRevision, StringComparison.OrdinalIgnoreCase);
                if (exactRevisionProvided && !localHeadMatches)
                    throw new InvalidOperationException("Local PackageSet HEAD does not match the requested exact revision.");
                exactRevision = localHead;
            }
            else
            {
                AcquireExactCheckout(checkout, request.PackageSetRepository, exactRevision, cancellation);
            }

            PackageSetDocument document = WindowsPackageSetEnvironmentSupport.ReadPackageSetDocument(checkout);
            CaptureAuthorityEvidence(checkout, document, exactRevision);
            InitializeDeclaredSources(checkout, document, cancellation);
            VerifyAuthorityEvidence(checkout, document);
            ValidateCleanPackageSet(checkout);
            string finalHead = IO.Git(checkout, "rev-parse", "HEAD");
            if (!finalHead.Equals(exactRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PackageSet HEAD changed while acquiring declared sources.");
            string repository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(checkout);
            return new PackageSetResolution(checkout, repository, finalHead);
        }

        public AuthoritativePackageSetLayout ReadAuthoritativeLayout(UseExistingPackageSetRequest request, PackageSetResolution resolution)
        {
            ValidateResolvedPackageSet(request, resolution);
            PackageSetDocument document = WindowsPackageSetEnvironmentSupport.ReadPackageSetDocument(resolution.CheckoutPath);
            VerifyAuthorityEvidence(resolution.CheckoutPath, document);
            string discoveredProjectGitRoot = IO.Git(request.ProjectRoot, "rev-parse", "--show-toplevel");
            string projectGitRoot = IO.Full(discoveredProjectGitRoot);
            ValidateTargetRepository(document, projectGitRoot);

            string sourceRoot = PackageRelativePath.Normalize(document.sourceRoot);
            string projectionRoot = WindowsPackageSetEnvironmentSupport.RequireOrdinaryAssetsProjectionRoot(request.ProjectRoot, document.managedProjectionRoot);
            List<AuthoritativePackageSetEntry> entries = new List<AuthoritativePackageSetEntry>();
            foreach (PackageSetEntryDocument documentEntry in document.entries)
            {
                string identity = PackageRelativePath.Normalize(documentEntry.identity);
                bool identityIsPath = identity.Contains("/");
                if (identityIsPath) throw new InvalidOperationException("PackageSet entry identity must be canonical: " + identity);
                string sourcePath = PackageRelativePath.Normalize(documentEntry.sourcePath);
                string projectionName = WindowsPackageSetEnvironmentSupport.Projection(documentEntry);
                string declaredProjectionPath = projectionRoot + "/" + projectionName;
                string projectionPath = PackageRelativePath.Normalize(declaredProjectionPath);
                DevelopmentSourceKind kind = WindowsPackageSetEnvironmentSupport.ParseKind(documentEntry.kind);
                if (kind != DevelopmentSourceKind.GitSubmodule)
                    throw new InvalidOperationException(identity + ": Use Existing PackageSet requires a portable gitlink-backed source. LocalDraft is Create-only until published.");
                bool runtime = WindowsPackageSetEnvironmentSupport.IsRuntimeIdentity(identity);
                if (documentEntry.bootstrapRuntime != runtime)
                    throw new InvalidOperationException(identity + ": manifest runtime classification does not match the canonical identity.");
                string source = IO.Under(resolution.CheckoutPath, sourcePath);
                string sourceRepository;
                string exactSourceRevision = ReadExactSourceRevision(resolution.CheckoutPath, sourcePath, source, kind, out sourceRepository);
                AuthoritativePackageSetEntry entry = new AuthoritativePackageSetEntry(identity, sourcePath, projectionPath, sourceRepository, exactSourceRevision, kind, runtime);
                entries.Add(entry);
            }
            return new AuthoritativePackageSetLayout(sourceRoot, projectionRoot, entries);
        }

        public ObservedPackageSetEntry ObserveEntry(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetEntry entry)
        {
            try
            {
                return ObserveEntryCore(request, resolution, entry);
            }
            catch (Exception error)
            {
                DevelopmentSourceObservation conflict = new DevelopmentSourceObservation(false, string.Empty, entry.ExactRevision, false, false, true, false, false, false, DevelopmentProjectionState.Conflict, error.Message);
                return new ObservedPackageSetEntry(entry.ProjectionPath, entry.Repository, conflict);
            }
        }

        private ObservedPackageSetEntry ObserveEntryCore(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetEntry entry)
        {
            string discoveredProjectGitRoot = IO.Git(request.ProjectRoot, "rev-parse", "--show-toplevel");
            string projectGitRoot = IO.Full(discoveredProjectGitRoot);
            string source = IO.Under(resolution.CheckoutPath, entry.SourcePath);
            bool sourceDirectoryExists = Directory.Exists(source);
            bool sourceAvailable = false;
            if (sourceDirectoryExists) sourceAvailable = WindowsPackageSetEnvironmentSupport.IsIndependentGitCheckout(source);
            string sourceHead = sourceAvailable ? IO.Git(source, "rev-parse", "HEAD") : string.Empty;
            string sourceRepository = sourceAvailable ? WindowsPackageSetEnvironmentSupport.CheckoutRemote(source) : "source-unavailable";
            string sourceStatus = sourceAvailable ? IO.Git(source, "status", "--porcelain") : string.Empty;
            bool sourceDirty = sourceAvailable && sourceStatus.Length != 0;
            string evidence = sourceAvailable ? IO.HashTree(source) : "source-unavailable";
            bool sourceAligned = sourceAvailable && sourceHead.Equals(entry.ExactRevision, StringComparison.OrdinalIgnoreCase);
            string canonicalRelative = entry.ProjectionPath;
            string canonical = IO.Under(request.ProjectRoot, canonicalRelative);
            string originalRelative = canonicalRelative;
            string original = canonical;
            DevelopmentProjectionState state = DevelopmentProjectionState.Absent;
            bool metadataOwned = !IO.Exists(canonical + ".meta");
            bool pathOwned = IsOwnedAssetsPath(request.ProjectRoot, canonical);
            bool contentMatches = sourceAligned;
            bool projectChanges = false;

            if (IO.Reparse(canonical))
            {
                string canonicalMetadata = canonical + ".meta";
                bool metadataOccupied = IO.Exists(canonicalMetadata);
                metadataOwned = !metadataOccupied;
                if (metadataOccupied) metadataOwned = File.Exists(canonicalMetadata);
                string junctionTarget = IO.JunctionTarget(canonical);
                string fullSource = IO.Full(source);
                bool correctTarget = junctionTarget.Equals(fullSource, StringComparison.OrdinalIgnoreCase);
                state = correctTarget ? DevelopmentProjectionState.CorrectJunction : DevelopmentProjectionState.Conflict;
                contentMatches = sourceAligned && correctTarget;
                projectChanges = !correctTarget;
            }
            else if (Directory.Exists(canonical))
            {
                string canonicalMetadata = canonical + ".meta";
                bool metadataOccupied = IO.Exists(canonicalMetadata);
                metadataOwned = !metadataOccupied;
                if (metadataOccupied) metadataOwned = File.Exists(canonicalMetadata);
                string canonicalProjectRelative = IO.Relative(projectGitRoot, canonical);
                string canonicalSection = WindowsPackageSetEnvironmentSupport.FindSubmoduleSection(projectGitRoot, canonicalProjectRelative);
                if (!string.IsNullOrWhiteSpace(canonicalSection))
                {
                    bool validBootstrap = false;
                    if (entry.BootstrapRuntime) validBootstrap = IsMatchingBootstrapCheckout(projectGitRoot, source, canonical, canonicalProjectRelative, entry.ExactRevision);
                    state = validBootstrap ? DevelopmentProjectionState.BootstrapGitlink : DevelopmentProjectionState.Conflict;
                    contentMatches = sourceAligned && validBootstrap;
                    projectChanges = !contentMatches;
                }
                else
                {
                    bool undeclaredGitOwnership = WindowsPackageSetEnvironmentSupport.ContainsGitOwnership(canonical);
                    state = undeclaredGitOwnership ? DevelopmentProjectionState.Conflict : DevelopmentProjectionState.PhysicalSnapshot;
                    string canonicalEvidence = string.Empty;
                    if (!undeclaredGitOwnership && sourceAligned) canonicalEvidence = IO.HashTree(canonical);
                    bool canonicalEvidenceMatches = canonicalEvidence.Equals(evidence, StringComparison.Ordinal);
                    contentMatches = !undeclaredGitOwnership && sourceAligned && canonicalEvidenceMatches;
                    projectChanges = !contentMatches;
                }
            }
            else
            {
                bool canonicalExists = IO.Exists(canonical);
                bool canonicalMetadataExists = IO.Exists(canonical + ".meta");
                if (canonicalExists || canonicalMetadataExists)
                {
                    state = DevelopmentProjectionState.Conflict;
                    metadataOwned = false;
                    contentMatches = false;
                    projectChanges = true;
                }
                else if (entry.BootstrapRuntime)
                {
                    bool ambiguous;
                    string bootstrapRelative = FindBootstrapSourcePath(request.ProjectRoot, projectGitRoot, source, entry.ExactRevision, out ambiguous);
                    if (ambiguous)
                    {
                        state = DevelopmentProjectionState.Conflict;
                        pathOwned = false;
                        contentMatches = false;
                        projectChanges = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(bootstrapRelative))
                    {
                        originalRelative = bootstrapRelative;
                        original = IO.Under(request.ProjectRoot, originalRelative);
                        state = DevelopmentProjectionState.BootstrapGitlink;
                        string originalMetadata = original + ".meta";
                        bool metadataOccupied = IO.Exists(originalMetadata);
                        metadataOwned = !metadataOccupied;
                        if (metadataOccupied) metadataOwned = File.Exists(originalMetadata);
                        bool originalOwned = IsOwnedAssetsPath(request.ProjectRoot, original);
                        bool canonicalOwned = IsOwnedAssetsPath(request.ProjectRoot, canonical);
                        pathOwned = originalOwned && canonicalOwned;
                        contentMatches = sourceAligned;
                        projectChanges = !contentMatches;
                    }
                }
            }

            bool indexAlignmentVerified = VerifyIndexAlignment(request.ProjectRoot, projectGitRoot, source, original, canonical, entry, state);
            if (state == DevelopmentProjectionState.PhysicalSnapshot || state == DevelopmentProjectionState.BootstrapGitlink || state == DevelopmentProjectionState.CorrectJunction)
            {
                contentMatches = sourceAligned && indexAlignmentVerified;
                projectChanges = !contentMatches;
            }
            DevelopmentSourceObservation observation = new DevelopmentSourceObservation(sourceAvailable, sourceHead, entry.ExactRevision, sourceDirty, contentMatches, projectChanges, metadataOwned, pathOwned, indexAlignmentVerified, state, evidence);
            return new ObservedPackageSetEntry(originalRelative, sourceRepository, observation);
        }

        public PackageSetModeObservation ObservePackageSetMode(UseExistingPackageSetRequest request, PackageSetResolution resolution, AuthoritativePackageSetLayout authoritativeLayout)
        {
            string discoveredProjectGitRoot = IO.Git(request.ProjectRoot, "rev-parse", "--show-toplevel");
            string projectGitRoot = IO.Full(discoveredProjectGitRoot);
            ObservedProjectPackageSetBinding projectBinding = ObserveProjectBinding(request.ProjectRoot, projectGitRoot);
            ObservedRuntimeBindings runtimeBindings = ObserveRuntimeBindings(request.ProjectRoot, projectGitRoot);
            return new PackageSetModeObservation(projectBinding, runtimeBindings);
        }

        private static ObservedProjectPackageSetBinding ObserveProjectBinding(string projectRoot, string projectGitRoot)
        {
            string relative = PackageSetProjectConfiguration.SharedRelativePath;
            string absolute = IO.Under(projectGitRoot, relative);
            bool exists = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(absolute);
            bool regularFile = WindowsPackageSetEnvironmentSupport.IsRegularDurableFile(absolute);
            WindowsPackageSetEnvironmentSupport.DurableFileSnapshot? snapshot = null;
            try
            {
                if (regularFile) snapshot = WindowsPackageSetEnvironmentSupport.OpenDurableFileSnapshot(absolute);
                bool trackedInIndex;
                bool indexMatchesWorktree;
                WindowsPackageSetEnvironmentSupport.ObserveIndexFile(projectGitRoot, relative, regularFile, "100644", out trackedInIndex, out indexMatchesWorktree);
                string fingerprint = snapshot == null ? WindowsPackageSetEnvironmentSupport.DurablePathFingerprint(absolute) : snapshot.Fingerprint;
                if (!exists)
                {
                    List<string> absentDiagnostics = new List<string>();
                    DurableBindingObservationState absentState = DurableBindingObservationState.Absent;
                    if (trackedInIndex || !indexMatchesWorktree)
                    {
                        absentState = DurableBindingObservationState.Invalid;
                        absentDiagnostics.Add("The worktree file is absent but the Project Git index still contains binding state.");
                    }
                    return new ObservedProjectPackageSetBinding(absentState, 0, string.Empty, string.Empty, string.Empty, string.Empty, false, fingerprint, trackedInIndex, indexMatchesWorktree, absentDiagnostics);
                }

                List<string> diagnostics = new List<string>();
                if (!regularFile) diagnostics.Add("The Project PackageSet binding path must be an ordinary file.");
                RawProjectPackageSetBinding? document = null;
                try
                {
                    if (snapshot != null)
                    {
                        bool validShape = StrictBindingJsonShape.ValidateProject(snapshot.Text, diagnostics);
                        if (validShape) document = JsonUtility.FromJson<RawProjectPackageSetBinding>(snapshot.Text);
                    }
                }
                catch (Exception error)
                {
                    string diagnostic = "Malformed JSON: " + error.Message;
                    diagnostics.Add(diagnostic);
                }
                if (document == null)
                {
                    diagnostics.Add("The Project PackageSet binding document is empty or malformed.");
                    document = new RawProjectPackageSetBinding();
                }
                if (document.schemaVersion != 3) diagnostics.Add("schemaVersion must be 3.");
                bool trackingRefMissing = string.IsNullOrWhiteSpace(document.trackingRef);
                if (trackingRefMissing) diagnostics.Add("trackingRef is required.");
                bool exactRevision = IsExactGitObjectId(document.resolvedRevision);
                if (!exactRevision) diagnostics.Add("resolvedRevision must be an exact Git object id.");
                ValidateObservedRepository(document.repository, document.needsPublish, diagnostics);
                ValidateObservedProjectionRoot(projectRoot, document.managedProjectionRoot, diagnostics);
                DurableBindingObservationState state = diagnostics.Count == 0 ? DurableBindingObservationState.Valid : DurableBindingObservationState.Invalid;
                return new ObservedProjectPackageSetBinding(state, document.schemaVersion, document.repository, document.trackingRef, document.resolvedRevision, document.managedProjectionRoot, document.needsPublish, fingerprint, trackedInIndex, indexMatchesWorktree, diagnostics);
            }
            finally
            {
                if (snapshot != null) snapshot.Dispose();
            }
        }

        private static ObservedRuntimeBindings ObserveRuntimeBindings(string projectRoot, string projectGitRoot)
        {
            const string relative = "PackageManagement/runtime.bindings.json";
            string absolute = IO.Under(projectGitRoot, relative);
            bool exists = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(absolute);
            bool regularFile = WindowsPackageSetEnvironmentSupport.IsRegularDurableFile(absolute);
            WindowsPackageSetEnvironmentSupport.DurableFileSnapshot? snapshot = null;
            try
            {
                if (regularFile) snapshot = WindowsPackageSetEnvironmentSupport.OpenDurableFileSnapshot(absolute);
                bool trackedInIndex;
                bool indexMatchesWorktree;
                WindowsPackageSetEnvironmentSupport.ObserveIndexFile(projectGitRoot, relative, regularFile, "100644", out trackedInIndex, out indexMatchesWorktree);
                string fingerprint = snapshot == null ? WindowsPackageSetEnvironmentSupport.DurablePathFingerprint(absolute) : snapshot.Fingerprint;
                if (!exists)
                {
                    List<string> absentDiagnostics = new List<string>();
                    DurableBindingObservationState absentState = DurableBindingObservationState.Absent;
                    if (trackedInIndex || !indexMatchesWorktree)
                    {
                        absentState = DurableBindingObservationState.Invalid;
                        absentDiagnostics.Add("The worktree file is absent but the Project Git index still contains runtime binding state.");
                    }
                    return new ObservedRuntimeBindings(absentState, 0, new ObservedRuntimeBindingEntry[0], fingerprint, trackedInIndex, indexMatchesWorktree, absentDiagnostics);
                }

                List<string> diagnostics = new List<string>();
                if (!regularFile) diagnostics.Add("The runtime binding path must be an ordinary file.");
                RawRuntimeBindings? document = null;
                try
                {
                    if (snapshot != null)
                    {
                        bool validShape = StrictBindingJsonShape.ValidateRuntime(snapshot.Text, diagnostics);
                        if (validShape) document = JsonUtility.FromJson<RawRuntimeBindings>(snapshot.Text);
                    }
                }
                catch (Exception error)
                {
                    string diagnostic = "Malformed JSON: " + error.Message;
                    diagnostics.Add(diagnostic);
                }
                if (document == null)
                {
                    diagnostics.Add("The runtime binding document is empty or malformed.");
                    document = new RawRuntimeBindings();
                }
                if (document.schemaVersion != 1) diagnostics.Add("schemaVersion must be 1.");
                List<ObservedRuntimeBindingEntry> entries = new List<ObservedRuntimeBindingEntry>();
                if (document.components == null)
                {
                    diagnostics.Add("components is required.");
                }
                else
                {
                    foreach (RawRuntimeBindingEntry component in document.components)
                    {
                        ObserveRuntimeBindingEntry(projectRoot, projectGitRoot, component, entries, diagnostics);
                    }
                }
                DurableBindingObservationState state = diagnostics.Count == 0 ? DurableBindingObservationState.Valid : DurableBindingObservationState.Invalid;
                return new ObservedRuntimeBindings(state, document.schemaVersion, entries, fingerprint, trackedInIndex, indexMatchesWorktree, diagnostics);
            }
            finally
            {
                if (snapshot != null) snapshot.Dispose();
            }
        }

        private static void ObserveRuntimeBindingEntry(string projectRoot, string projectGitRoot, RawRuntimeBindingEntry component, ICollection<ObservedRuntimeBindingEntry> entries, ICollection<string> diagnostics)
        {
            if (component == null)
            {
                diagnostics.Add("components contains a null entry.");
                return;
            }
            bool identityMissing = string.IsNullOrWhiteSpace(component.id);
            if (identityMissing) diagnostics.Add("Runtime binding identity is required.");
            bool repositoryMissing = string.IsNullOrWhiteSpace(component.repository);
            if (repositoryMissing) diagnostics.Add(component.id + ": repository is required.");
            else
            {
                try
                {
                    IO.RequireRemote(component.repository);
                }
                catch (Exception error)
                {
                    string diagnostic = component.id + ": repository is invalid: " + error.Message;
                    diagnostics.Add(diagnostic);
                }
            }

            string projectionPath = string.Empty;
            if (string.IsNullOrWhiteSpace(component.path))
            {
                diagnostics.Add(component.id + ": path is required.");
            }
            else
            {
                try
                {
                    string absolute = IO.Under(projectGitRoot, component.path);
                    bool insideProject = IO.Within(projectRoot, absolute);
                    if (!insideProject) diagnostics.Add(component.id + ": path is outside the Unity Project root.");
                    else projectionPath = IO.Relative(projectRoot, absolute);
                }
                catch (Exception error)
                {
                    string diagnostic = component.id + ": path is invalid: " + error.Message;
                    diagnostics.Add(diagnostic);
                }
            }
            ObservedRuntimeBindingEntry entry = new ObservedRuntimeBindingEntry(component.id, component.repository, projectionPath);
            entries.Add(entry);
        }

        private static void ValidateObservedRepository(string repository, bool needsPublish, ICollection<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                if (!needsPublish) diagnostics.Add("repository is required unless needsPublish is true.");
                return;
            }
            try
            {
                IO.RequireRemote(repository);
            }
            catch (Exception error)
            {
                string diagnostic = "repository is invalid: " + error.Message;
                diagnostics.Add(diagnostic);
            }
        }

        private static void ValidateObservedProjectionRoot(string projectRoot, string projectionRoot, ICollection<string> diagnostics)
        {
            try
            {
                WindowsPackageSetEnvironmentSupport.RequireOrdinaryAssetsProjectionRoot(projectRoot, projectionRoot);
            }
            catch (Exception error)
            {
                string diagnostic = "managedProjectionRoot is invalid: " + error.Message;
                diagnostics.Add(diagnostic);
            }
        }

        private static bool IsExactGitObjectId(string revision)
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

        private static bool VerifyIndexAlignment(string projectRoot, string projectGitRoot, string source, string original, string canonical, AuthoritativePackageSetEntry entry, DevelopmentProjectionState state)
        {
            string canonicalProjectRelative = IO.Relative(projectGitRoot, canonical);
            string canonicalSection = WindowsPackageSetEnvironmentSupport.FindSubmoduleSection(projectGitRoot, canonicalProjectRelative);
            bool canonicalSectionAbsent = string.IsNullOrWhiteSpace(canonicalSection);
            bool unexpectedRuntimeBootstrap = HasUnexpectedRuntimeBootstrap(projectRoot, projectGitRoot, entry, canonicalProjectRelative, string.Empty);
            if (state == DevelopmentProjectionState.Absent)
            {
                bool indexPathAbsent = WindowsPackageSetEnvironmentSupport.IndexPathAbsent(projectGitRoot, canonicalProjectRelative);
                bool relatedProjectSubmodule = HasRelatedProjectSubmodule(projectRoot, projectGitRoot, source, entry.ExactRevision);
                return canonicalSectionAbsent && indexPathAbsent && !relatedProjectSubmodule && !unexpectedRuntimeBootstrap;
            }
            if (state == DevelopmentProjectionState.PhysicalSnapshot)
            {
                bool ordinaryIndexMatches = WindowsPackageSetEnvironmentSupport.OrdinaryProjectionIndexMatches(projectGitRoot, source, canonical, canonicalProjectRelative, true);
                bool stalePhysicalBootstrap = entry.BootstrapRuntime && HasRelatedProjectSubmodule(projectRoot, projectGitRoot, source, entry.ExactRevision);
                return canonicalSectionAbsent && ordinaryIndexMatches && !stalePhysicalBootstrap && !unexpectedRuntimeBootstrap;
            }
            if (state == DevelopmentProjectionState.BootstrapGitlink)
            {
                string originalProjectRelative = IO.Relative(projectGitRoot, original);
                unexpectedRuntimeBootstrap = HasUnexpectedRuntimeBootstrap(projectRoot, projectGitRoot, entry, canonicalProjectRelative, originalProjectRelative);
                bool gitlinkMatches = WindowsPackageSetEnvironmentSupport.IndexGitlinkMatches(projectGitRoot, originalProjectRelative, entry.ExactRevision);
                bool metaExists = File.Exists(original + ".meta");
                bool metaMatches = WindowsPackageSetEnvironmentSupport.IndexFileMatchesWorktree(projectGitRoot, originalProjectRelative + ".meta", metaExists, "100644");
                bool sameDestination = original.Equals(canonical, StringComparison.OrdinalIgnoreCase);
                bool destinationAbsent = sameDestination;
                if (!sameDestination) destinationAbsent = WindowsPackageSetEnvironmentSupport.IndexPathAbsent(projectGitRoot, canonicalProjectRelative);
                bool metadataMatches = WindowsPackageSetEnvironmentSupport.ConversionMetadataMatches(projectGitRoot, source, original, originalProjectRelative);
                return gitlinkMatches && metaMatches && destinationAbsent && metadataMatches && !unexpectedRuntimeBootstrap;
            }
            if (state != DevelopmentProjectionState.CorrectJunction) return false;
            bool projectionMatches = WindowsPackageSetEnvironmentSupport.OrdinaryProjectionIndexMatches(projectGitRoot, source, canonical, canonicalProjectRelative, false, canonical, source);
            bool staleBootstrap = HasRelatedProjectSubmodule(projectRoot, projectGitRoot, source, entry.ExactRevision);
            return projectionMatches && canonicalSectionAbsent && !staleBootstrap && !unexpectedRuntimeBootstrap;
        }

        private static bool HasUnexpectedRuntimeBootstrap(string projectRoot, string projectGitRoot, AuthoritativePackageSetEntry entry, string canonicalProjectRelative, string allowedBootstrapRelative)
        {
            if (!entry.BootstrapRuntime) return false;
            string knownLegacyRelative = PackageRelativePath.Normalize("Assets/PackageManagement/" + entry.Identity);
            bool canonicalLegacyPath = knownLegacyRelative.Equals(canonicalProjectRelative, StringComparison.Ordinal);
            bool allowedLegacyPath = knownLegacyRelative.Equals(allowedBootstrapRelative, StringComparison.Ordinal);
            if (!canonicalLegacyPath && !allowedLegacyPath)
            {
                string knownLegacyPath = IO.Under(projectGitRoot, knownLegacyRelative);
                bool legacyPathExists = IO.Exists(knownLegacyPath);
                bool legacyMetadataExists = IO.Exists(knownLegacyPath + ".meta");
                if (legacyPathExists || legacyMetadataExists) return true;
            }
            List<ProjectSubmodule> submodules = ReadProjectSubmodules(projectGitRoot);
            foreach (ProjectSubmodule submodule in submodules)
            {
                if (submodule.Path.Equals(allowedBootstrapRelative, StringComparison.Ordinal)) continue;
                if (RuntimePathMatchesIdentity(submodule.Path, entry.Identity)) return true;
            }
            string stage = IO.Git(projectGitRoot, "ls-files", "--stage", "-z", "--", "Assets");
            string[] records = stage.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string record in records)
            {
                if (!record.StartsWith("160000 ", StringComparison.Ordinal)) continue;
                int tab = record.IndexOf('\t');
                if (tab <= 0) return true;
                string path = record.Substring(tab + 1);
                if (path.Equals(allowedBootstrapRelative, StringComparison.Ordinal)) continue;
                if (RuntimePathMatchesIdentity(path, entry.Identity)) return true;
            }
            return false;
        }

        private static bool RuntimePathMatchesIdentity(string relative, string identity)
        {
            if (string.IsNullOrWhiteSpace(relative)) return false;
            string normalized = PackageRelativePath.Normalize(relative);
            int slash = normalized.LastIndexOf('/');
            string leaf = slash < 0 ? normalized : normalized.Substring(slash + 1);
            return leaf.Equals(identity, StringComparison.Ordinal);
        }

        private static bool HasRelatedProjectSubmodule(string projectRoot, string projectGitRoot, string canonicalSource, string exactRevision)
        {
            string sourceRepository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(canonicalSource);
            string sourceIdentity = IO.RemoteIdentity(sourceRepository);
            List<ProjectSubmodule> submodules = ReadProjectSubmodules(projectGitRoot);
            foreach (ProjectSubmodule submodule in submodules)
            {
                bool pathMissing = string.IsNullOrWhiteSpace(submodule.Path);
                bool repositoryMissing = string.IsNullOrWhiteSpace(submodule.Repository);
                if (pathMissing || repositoryMissing) continue;
                string projectIdentity = IO.RemoteIdentity(submodule.Repository);
                if (!projectIdentity.Equals(sourceIdentity, StringComparison.Ordinal)) continue;
                string projectSource = IO.Under(projectGitRoot, submodule.Path);
                if (!IsOwnedAssetsPath(projectRoot, projectSource)) continue;
                return true;
            }
            string stage = IO.Git(projectGitRoot, "ls-files", "--stage", "-z", "--", "Assets");
            string[] records = stage.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            string exactGitlinkPrefix = "160000 " + exactRevision + " 0\t";
            foreach (string record in records)
            {
                if (record.StartsWith(exactGitlinkPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public string ExportOfflineApply(UseExistingPackageSetPlan plan)
        {
            PackageSetDocument document = WindowsPackageSetEnvironmentSupport.ReadPackageSetDocument(plan.Resolution.CheckoutPath);
            VerifyAuthorityEvidence(plan.Resolution.CheckoutPath, document);
            string projectGitRoot;
            string projectGitDirectory;
            WindowsPackageSetEnvironmentSupport.ResolveProjectGitLocations(plan.Request.ProjectRoot, out projectGitRoot, out projectGitDirectory);
            return WindowsOfflineApplyExporter.ExportExisting(plan, projectGitRoot, projectGitDirectory, helperPath, unityExecutable);
        }

        public void MarkOfflineApplyReady(string applyInstructionsPath)
        {
            WindowsOfflineApplyExporter.MarkReady(applyInstructionsPath);
        }

        private static void ValidateIndependentCheckout(string checkout)
        {
            bool checkoutExists = Directory.Exists(checkout);
            if (!checkoutExists) throw new InvalidOperationException("PackageSet checkout does not exist: " + checkout);
            if (!WindowsPackageSetEnvironmentSupport.IsIndependentGitCheckout(checkout))
                throw new InvalidOperationException("PackageSet path is not an independent Git checkout.");
        }

        private static void ValidateCleanPackageSet(string checkout)
        {
            string status = IO.Git(checkout, "status", "--porcelain");
            if (status.Length != 0)
                throw new InvalidOperationException("PackageSet checkout is dirty.");
        }

        private static void ValidateRepositoryIdentity(string checkout, string expectedRepository)
        {
            if (string.IsNullOrWhiteSpace(expectedRepository)) return;
            string actualRepository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(checkout);
            if (string.IsNullOrWhiteSpace(actualRepository))
                throw new InvalidOperationException("PackageSet checkout has no origin remote.");
            string expectedIdentity = IO.RemoteIdentity(expectedRepository);
            string actualIdentity = IO.RemoteIdentity(actualRepository);
            if (!actualIdentity.Equals(expectedIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("PackageSet checkout belongs to another repository.");
        }

        private static void AcquireExactCheckout(string checkout, string repository, string exactRevision, CancellationToken cancellation)
        {
            bool fresh = !IO.Exists(checkout);
            if (!fresh && Directory.Exists(checkout))
            {
                IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(checkout);
                IEnumerator<string> enumerator = entries.GetEnumerator();
                fresh = !enumerator.MoveNext();
                enumerator.Dispose();
            }
            if (fresh)
            {
                string? parent = Path.GetDirectoryName(checkout);
                bool parentMissing = string.IsNullOrWhiteSpace(parent);
                if (parentMissing) throw new InvalidOperationException("PackageSet checkout has no parent directory.");
                string checkoutParent = parent!;
                Directory.CreateDirectory(checkoutParent);
                string[] cloneArguments = { "clone", "--no-checkout", "--", repository, checkout };
                IO.Run("git", checkoutParent, cloneArguments, cancellation);
            }
            else
            {
                ValidateIndependentCheckout(checkout);
                ValidateCleanPackageSet(checkout);
                ValidateRepositoryIdentity(checkout, repository);
            }

            string resolvedRevision;
            try
            {
                resolvedRevision = IO.Git(checkout, "rev-parse", "--verify", "--end-of-options", exactRevision + "^{commit}");
            }
            catch (InvalidOperationException)
            {
                string[] fetchArguments = { "fetch", "--no-tags", "origin", exactRevision };
                IO.Run("git", checkout, fetchArguments, cancellation);
                resolvedRevision = IO.Git(checkout, "rev-parse", "--verify", "FETCH_HEAD^{commit}");
            }
            if (!resolvedRevision.Equals(exactRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved PackageSet commit differs from the requested exact revision.");
            string[] checkoutArguments = { "checkout", "--detach", resolvedRevision };
            IO.Run("git", checkout, checkoutArguments, cancellation);
            ValidateCleanPackageSet(checkout);
        }

        private void CaptureAuthorityEvidence(string checkout, PackageSetDocument document, string expectedRevision)
        {
            authorityRevision = IO.Git(checkout, "rev-parse", "--verify", "HEAD");
            if (!authorityRevision.Equals(expectedRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PackageSet HEAD changed before authority evidence could be captured.");
            ValidateDeclaredSourceMappings(checkout, document);
            manifestFingerprint = WindowsPackageSetEnvironmentSupport.ReadOrdinaryHeadFileEvidence(checkout, "package-set.json");
            gitmodulesFingerprint = WindowsPackageSetEnvironmentSupport.ReadOrdinaryHeadFileEvidence(checkout, ".gitmodules");
            gitlinksEvidence = ReadGitlinksEvidence(checkout, document);
        }

        private void VerifyAuthorityEvidence(string checkout, PackageSetDocument document)
        {
            ValidateDeclaredSourceMappings(checkout, document);
            string currentRevision = IO.Git(checkout, "rev-parse", "--verify", "HEAD");
            string currentManifest = WindowsPackageSetEnvironmentSupport.ReadOrdinaryHeadFileEvidence(checkout, "package-set.json");
            string currentGitmodules = WindowsPackageSetEnvironmentSupport.ReadOrdinaryHeadFileEvidence(checkout, ".gitmodules");
            string currentGitlinks = ReadGitlinksEvidence(checkout, document);
            if (!currentRevision.Equals(authorityRevision, StringComparison.OrdinalIgnoreCase) ||
                !currentManifest.Equals(manifestFingerprint, StringComparison.Ordinal) ||
                !currentGitmodules.Equals(gitmodulesFingerprint, StringComparison.Ordinal) ||
                !currentGitlinks.Equals(gitlinksEvidence, StringComparison.Ordinal))
                throw new InvalidOperationException("Use Existing PackageSet changed manifest or gitlink authority during preparation.");
        }

        private static void ValidateDeclaredSourceMappings(string checkout, PackageSetDocument document)
        {
            foreach (PackageSetEntryDocument entry in document.entries)
            {
                DevelopmentSourceKind kind = WindowsPackageSetEnvironmentSupport.ParseKind(entry.kind);
                if (kind != DevelopmentSourceKind.GitSubmodule) continue;
                string sourcePath = PackageRelativePath.Normalize(entry.sourcePath);
                ReadDeclaredSourceRepository(checkout, sourcePath);
            }
        }

        private static string ReadGitlinksEvidence(string checkout, PackageSetDocument document)
        {
            List<string> records = new List<string>();
            foreach (PackageSetEntryDocument entry in document.entries)
            {
                DevelopmentSourceKind kind = WindowsPackageSetEnvironmentSupport.ParseKind(entry.kind);
                if (kind != DevelopmentSourceKind.GitSubmodule) continue;
                string sourcePath = PackageRelativePath.Normalize(entry.sourcePath);
                string exactRevision = IO.Gitlink(checkout, sourcePath);
                string record = entry.identity + "=" + exactRevision;
                records.Add(record);
            }
            records.Sort(StringComparer.Ordinal);
            string[] recordItems = records.ToArray();
            return string.Join("\n", recordItems);
        }

        private static void InitializeDeclaredSources(string checkout, PackageSetDocument document, CancellationToken cancellation)
        {
            foreach (PackageSetEntryDocument entry in document.entries)
            {
                cancellation.ThrowIfCancellationRequested();
                DevelopmentSourceKind kind = WindowsPackageSetEnvironmentSupport.ParseKind(entry.kind);
                string sourcePath = PackageRelativePath.Normalize(entry.sourcePath);
                string source = IO.Under(checkout, sourcePath);
                bool sourceOccupied = IO.Exists(source);
                if (sourceOccupied && IO.Reparse(source))
                    throw new InvalidOperationException(entry.identity + ": existing canonical source path is a reparse point.");
                if (kind == DevelopmentSourceKind.GitSubmodule)
                {
                    string exactRevision = IO.Gitlink(checkout, sourcePath);
                    if (string.IsNullOrWhiteSpace(exactRevision))
                        throw new InvalidOperationException(entry.identity + ": PackageSet source is not an exact committed gitlink.");
                    bool sourceDirectoryExists = Directory.Exists(source);
                    bool independentCheckout = sourceDirectoryExists && WindowsPackageSetEnvironmentSupport.IsIndependentGitCheckout(source);
                    if (independentCheckout) WindowsPackageSetEnvironmentSupport.RequirePackageSetOwnedCheckout(checkout, source, entry.identity);
                    string existingStatus = independentCheckout ? IO.Git(source, "status", "--porcelain") : string.Empty;
                    if (independentCheckout && existingStatus.Length != 0)
                        throw new InvalidOperationException(entry.identity + ": existing PackageSet source checkout is dirty.");
                    string[] syncArguments = { "submodule", "sync", "--", sourcePath };
                    IO.Run("git", checkout, syncArguments, cancellation);
                    string[] updateArguments = { "submodule", "update", "--init", "--checkout", "--", sourcePath };
                    IO.Run("git", checkout, updateArguments, cancellation);
                    WindowsPackageSetEnvironmentSupport.RequirePackageSetOwnedCheckout(checkout, source, entry.identity);
                    string sourceHead = IO.Git(source, "rev-parse", "HEAD");
                    string sourceStatus = IO.Git(source, "status", "--porcelain");
                    bool revisionMatches = sourceHead.Equals(exactRevision, StringComparison.OrdinalIgnoreCase);
                    if (!revisionMatches || sourceStatus.Length != 0)
                        throw new InvalidOperationException(entry.identity + ": canonical source does not match the PackageSet gitlink.");
                }
                else
                {
                    bool sourceDirectoryExists = Directory.Exists(source);
                    bool independentCheckout = sourceDirectoryExists && WindowsPackageSetEnvironmentSupport.IsIndependentGitCheckout(source);
                    if (!independentCheckout)
                        throw new InvalidOperationException(entry.identity + ": LocalDraft source is unavailable in this local PackageSet checkout.");
                    WindowsPackageSetEnvironmentSupport.RequirePackageSetOwnedCheckout(checkout, source, entry.identity);
                    string sourceStatus = IO.Git(source, "status", "--porcelain");
                    if (sourceStatus.Length != 0)
                        throw new InvalidOperationException(entry.identity + ": LocalDraft source is dirty.");
                }
            }
        }

        private void ValidateResolvedPackageSet(UseExistingPackageSetRequest request, PackageSetResolution resolution)
        {
            string checkout = IO.Full(resolution.CheckoutPath);
            string requestedCheckout = IO.Full(request.PackageSetCheckoutPath);
            if (!checkout.Equals(requestedCheckout, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved PackageSet checkout path differs from the request.");
            ValidateIndependentCheckout(checkout);
            ValidateCleanPackageSet(checkout);
            string head = IO.Git(checkout, "rev-parse", "HEAD");
            if (!head.Equals(resolution.ExactRevision, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved PackageSet HEAD changed before planning.");
            ValidateRepositoryIdentity(checkout, request.PackageSetRepository);
        }

        private static void ValidateTargetRepository(PackageSetDocument document, string projectGitRoot)
        {
            if (document.target == null || string.IsNullOrWhiteSpace(document.target.repository))
                throw new InvalidOperationException("PackageSet target repository is required.");
            string projectRemote = WindowsPackageSetEnvironmentSupport.ProjectRemote(projectGitRoot);
            if (string.IsNullOrWhiteSpace(projectRemote))
                throw new InvalidOperationException("Project origin repository identity is required.");
            string targetIdentity = IO.RemoteIdentity(document.target.repository);
            string projectIdentity = IO.RemoteIdentity(projectRemote);
            if (!targetIdentity.Equals(projectIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("PackageSet target repository does not match this Project origin.");
        }

        private static string ReadExactSourceRevision(string checkout, string sourcePath, string source, DevelopmentSourceKind kind, out string sourceRepository)
        {
            sourceRepository = string.Empty;
            WindowsPackageSetEnvironmentSupport.RequirePackageSetOwnedCheckout(checkout, source, sourcePath);
            string sourceStatus = IO.Git(source, "status", "--porcelain");
            if (sourceStatus.Length != 0)
                throw new InvalidOperationException("Canonical PackageSet source is dirty: " + sourcePath);
            if (!WindowsPackageSetEnvironmentSupport.WorkingTreeMatchesHead(source))
                throw new InvalidOperationException("Canonical PackageSet source worktree bytes do not match its exact Git tree: " + sourcePath);
            RequireOnlyIgnoredMetadata(source, sourcePath);
            string sourceHead = IO.Git(source, "rev-parse", "HEAD");
            if (kind == DevelopmentSourceKind.GitSubmodule)
            {
                string exactPin = IO.Gitlink(checkout, sourcePath);
                bool exactPinMissing = string.IsNullOrWhiteSpace(exactPin);
                bool sourceHeadMatches = sourceHead.Equals(exactPin, StringComparison.OrdinalIgnoreCase);
                if (exactPinMissing || !sourceHeadMatches)
                    throw new InvalidOperationException("Canonical source HEAD does not match its PackageSet gitlink: " + sourcePath);
                string declaredRepository = ReadDeclaredSourceRepository(checkout, sourcePath);
                string actualRepository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(source);
                IO.RequireRemote(declaredRepository);
                IO.RequireRemote(actualRepository);
                string declaredIdentity = IO.RemoteIdentity(declaredRepository);
                string actualIdentity = IO.RemoteIdentity(actualRepository);
                if (!declaredIdentity.Equals(actualIdentity, StringComparison.Ordinal))
                    throw new InvalidOperationException("Canonical source origin does not match PackageSet .gitmodules authority: " + sourcePath);
                if (!declaredRepository.Equals(actualRepository, StringComparison.Ordinal))
                    throw new InvalidOperationException("Canonical source origin spelling does not exactly match PackageSet .gitmodules authority: " + sourcePath);
                sourceRepository = declaredRepository;
                return exactPin;
            }
            sourceRepository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(source);
            return sourceHead;
        }

        private static void RequireOnlyIgnoredMetadata(string source, string sourcePath)
        {
            string ignoredOutput = IO.Git(source, "ls-files", "--others", "--ignored", "--exclude-standard", "-z");
            string[] ignoredPaths = ignoredOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string ignoredPath in ignoredPaths)
            {
                if (ignoredPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                throw new InvalidOperationException("Canonical PackageSet source contains ignored non-metadata outside its exact revision: " + sourcePath + "/" + ignoredPath);
            }
        }

        private static string ReadDeclaredSourceRepository(string checkout, string sourcePath)
        {
            string modulesPath = Path.Combine(checkout, ".gitmodules");
            bool modulesExists = File.Exists(modulesPath);
            if (!modulesExists) throw new InvalidOperationException("PackageSet .gitmodules is missing.");
            IReadOnlyList<WindowsPackageSetEnvironmentSupport.GitmodulesSection> sections = WindowsPackageSetEnvironmentSupport.ReadGitmodules(checkout);
            string repository = string.Empty;
            int matches = 0;
            foreach (WindowsPackageSetEnvironmentSupport.GitmodulesSection section in sections)
            {
                if (!section.Path.Equals(sourcePath, StringComparison.Ordinal)) continue;
                repository = section.Repository;
                matches++;
            }
            if (matches != 1) throw new InvalidOperationException("PackageSet gitlink must map to exactly one .gitmodules path and url: " + sourcePath);
            return repository;
        }

        private static bool IsOwnedAssetsPath(string projectRoot, string candidate)
        {
            string assetsPath = Path.Combine(projectRoot, "Assets");
            string assets = IO.Full(assetsPath);
            return IO.Within(assets, candidate);
        }

        private static bool IsMatchingBootstrapCheckout(string projectGitRoot, string canonicalSource, string projectSource, string projectRelative, string exactRevision)
        {
            if (!WindowsPackageSetEnvironmentSupport.IsIndependentGitCheckout(projectSource)) return false;
            RequireOnlyIgnoredMetadata(projectSource, projectRelative);
            string projectPin = IO.Gitlink(projectGitRoot, projectRelative);
            if (!projectPin.Equals(exactRevision, StringComparison.OrdinalIgnoreCase)) return false;
            string projectHead = IO.Git(projectSource, "rev-parse", "HEAD");
            if (!projectHead.Equals(exactRevision, StringComparison.OrdinalIgnoreCase)) return false;
            string projectStatus = IO.Git(projectSource, "status", "--porcelain");
            if (projectStatus.Length != 0) return false;
            if (!WindowsPackageSetEnvironmentSupport.WorkingTreeMatchesHead(projectSource)) return false;
            string sourceRepository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(canonicalSource);
            string projectRepository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(projectSource);
            bool sourceRepositoryMissing = string.IsNullOrWhiteSpace(sourceRepository);
            bool projectRepositoryMissing = string.IsNullOrWhiteSpace(projectRepository);
            if (sourceRepositoryMissing || projectRepositoryMissing) return false;
            string declaredRepository = ProjectSubmoduleRepository(projectGitRoot, projectRelative);
            if (string.IsNullOrWhiteSpace(declaredRepository)) return false;
            string sourceIdentity = IO.RemoteIdentity(sourceRepository);
            string projectIdentity = IO.RemoteIdentity(projectRepository);
            string declaredIdentity = IO.RemoteIdentity(declaredRepository);
            bool projectIdentityMatches = sourceIdentity.Equals(projectIdentity, StringComparison.Ordinal);
            bool declaredIdentityMatches = sourceIdentity.Equals(declaredIdentity, StringComparison.Ordinal);
            bool identitiesMatch = projectIdentityMatches && declaredIdentityMatches;
            bool projectDeclarationMatches = sourceRepository.Equals(projectRepository, StringComparison.Ordinal);
            bool packageSetDeclarationMatches = sourceRepository.Equals(declaredRepository, StringComparison.Ordinal);
            bool declarationsMatch = projectDeclarationMatches && packageSetDeclarationMatches;
            return identitiesMatch && declarationsMatch;
        }

        private static string ProjectSubmoduleRepository(string projectGitRoot, string projectRelative)
        {
            List<ProjectSubmodule> submodules = ReadProjectSubmodules(projectGitRoot);
            string repository = string.Empty;
            int matchingSections = 0;
            foreach (ProjectSubmodule submodule in submodules)
            {
                if (!submodule.Path.Equals(projectRelative, StringComparison.Ordinal)) continue;
                repository = submodule.Repository;
                matchingSections++;
            }
            return matchingSections == 1 ? repository : string.Empty;
        }

        private static string FindBootstrapSourcePath(string projectRoot, string projectGitRoot, string canonicalSource, string exactRevision, out bool ambiguous)
        {
            ambiguous = false;
            List<ProjectSubmodule> submodules = ReadProjectSubmodules(projectGitRoot);
            string sourceRepository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(canonicalSource);
            string sourceIdentity = IO.RemoteIdentity(sourceRepository);
            List<string> matches = new List<string>();
            bool relatedMismatch = false;
            foreach (ProjectSubmodule submodule in submodules)
            {
                bool pathMissing = string.IsNullOrWhiteSpace(submodule.Path);
                bool repositoryMissing = string.IsNullOrWhiteSpace(submodule.Repository);
                if (pathMissing || repositoryMissing) continue;
                string projectIdentity = IO.RemoteIdentity(submodule.Repository);
                if (!projectIdentity.Equals(sourceIdentity, StringComparison.Ordinal)) continue;
                string projectSource = IO.Under(projectGitRoot, submodule.Path);
                if (!IsOwnedAssetsPath(projectRoot, projectSource)) continue;
                if (IsMatchingBootstrapCheckout(projectGitRoot, canonicalSource, projectSource, submodule.Path, exactRevision))
                {
                    string projectRelative = IO.Relative(projectRoot, projectSource);
                    matches.Add(projectRelative);
                }
                else
                    relatedMismatch = true;
            }
            if (matches.Count == 1 && !relatedMismatch) return matches[0];
            ambiguous = matches.Count > 1 || relatedMismatch;
            return string.Empty;
        }

        private static List<ProjectSubmodule> ReadProjectSubmodules(string projectGitRoot)
        {
            List<ProjectSubmodule> result = new List<ProjectSubmodule>();
            IReadOnlyList<WindowsPackageSetEnvironmentSupport.GitmodulesSection> sections = WindowsPackageSetEnvironmentSupport.ReadGitmodules(projectGitRoot);
            foreach (WindowsPackageSetEnvironmentSupport.GitmodulesSection section in sections)
            {
                ProjectSubmodule submodule = new ProjectSubmodule();
                submodule.Section = section.SectionKey;
                submodule.Path = section.Path;
                submodule.Repository = section.Repository;
                result.Add(submodule);
            }
            result.Sort(CompareSubmodules);
            return result;
        }

        private static int CompareSubmodules(ProjectSubmodule first, ProjectSubmodule second)
        {
            return StringComparer.Ordinal.Compare(first.Path, second.Path);
        }
    }
}
