using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CraftyRacoon.PackageManagement.Development;
using CraftyRacoon.PackageManagement.Framework.Development;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IO = CraftyRacoon.PackageManagement.Unity3D.Editor.Development.DevelopmentBootstrapIO;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    internal sealed class DevelopmentBootstrapWindow : EditorWindow
    {
        private enum WorkflowKind
        {
            CreatePackageSet,
            UseExistingPackageSet
        }

        private sealed class Candidate
        {
            internal bool Selected;
            internal string Identity = string.Empty;
            internal string ProjectSourcePath = string.Empty;
            internal DevelopmentSourceKind Kind;
        }

        private sealed class PreparedWorkflow
        {
            internal PreparedWorkflow(CreatePackageSetPreparation preparation)
            {
                ExactRevision = preparation.Resolution.ExactRevision;
                Entries = preparation.Plan.Entries;
                ApplyInstructionsPath = preparation.ApplyInstructionsPath;
                AlreadyConfigured = false;
            }

            internal PreparedWorkflow(UseExistingPackageSetPreparation preparation)
            {
                ExactRevision = preparation.Plan.Resolution.ExactRevision;
                Entries = preparation.Plan.Entries;
                ApplyInstructionsPath = preparation.ApplyInstructionsPath;
                AlreadyConfigured = preparation.Plan.AlreadyConfigured;
            }

            internal string ExactRevision { get; }
            internal IReadOnlyList<PackageSetEntryPlan> Entries { get; }
            internal string ApplyInstructionsPath { get; }
            internal bool AlreadyConfigured { get; }
        }

        private sealed class PreparationWorkItem
        {
            private readonly CreatePackageSetRequest? createRequest;
            private readonly UseExistingPackageSetRequest? existingRequest;
            private readonly string helperPath;
            private readonly string unityExecutable;
            private readonly CancellationToken cancellation;

            internal PreparationWorkItem(CreatePackageSetRequest request, string helperPath, string unityExecutable, CancellationToken cancellation)
            {
                createRequest = request;
                existingRequest = null;
                this.helperPath = helperPath;
                this.unityExecutable = unityExecutable;
                this.cancellation = cancellation;
            }

            internal PreparationWorkItem(UseExistingPackageSetRequest request, string helperPath, string unityExecutable, CancellationToken cancellation)
            {
                createRequest = null;
                existingRequest = request;
                this.helperPath = helperPath;
                this.unityExecutable = unityExecutable;
                this.cancellation = cancellation;
            }

            internal PreparedWorkflow Execute()
            {
                if (createRequest != null)
                {
                    WindowsCreatePackageSetEnvironment environment = new WindowsCreatePackageSetEnvironment(helperPath, unityExecutable);
                    IPackageSetCreator creator = new PackageSetCreator(environment);
                    CreatePackageSetPreparation preparation = creator.Prepare(createRequest, cancellation);
                    return new PreparedWorkflow(preparation);
                }
                if (existingRequest == null) throw new InvalidOperationException("Workflow request is unavailable.");
                WindowsUseExistingPackageSetEnvironment existingEnvironment = new WindowsUseExistingPackageSetEnvironment(helperPath, unityExecutable);
                IProjectPackageSetConverter converter = new ProjectPackageSetConverter(existingEnvironment);
                UseExistingPackageSetPreparation existingPreparation = converter.Prepare(existingRequest, cancellation);
                return new PreparedWorkflow(existingPreparation);
            }
        }

        private static readonly string[] WorkflowLabels = { "Create PackageSet", "Use Existing PackageSet" };
        private static readonly string[] AcquisitionLabels = { "Open local checkout", "Clone/open exact revision", "Resolve tracking ref once" };
        private readonly List<Candidate> candidates = new List<Candidate>();
        private WorkflowKind workflow = WorkflowKind.UseExistingPackageSet;
        private PackageSetAcquisitionStrategy acquisitionStrategy = PackageSetAcquisitionStrategy.OpenLocalCheckout;
        private bool acquisitionInitialized;
        private string packageSetRoot = string.Empty;
        private string createRepository = string.Empty;
        private string createTrackingRef = "main";
        private string repository = string.Empty;
        private string trackingRef = "main";
        private string resolvedRevision = string.Empty;
        private string sourceRoot = "Sources";
        private string projectionRoot = "Assets/CraftyRacoon/Managed";
        private ProjectPackageSetDocument projectConfiguration = new ProjectPackageSetDocument();
        private string message = string.Empty;
        private Vector2 scroll;
        private Task<PreparedWorkflow>? task;
        private CancellationTokenSource? cancellation;
        private PreparedWorkflow? prepared;

        internal static void Open()
        {
            GetWindow<DevelopmentBootstrapWindow>("PackageSet Initialization");
        }

        private static string ProjectRoot
        {
            get
            {
                string parent = Path.Combine(Application.dataPath, "..");
                return Path.GetFullPath(parent);
            }
        }

        private void OnEnable()
        {
            RunGuarded(LoadConfiguration);
        }

        private void OnDisable()
        {
            if (cancellation != null) cancellation.Cancel();
        }

        private void Update()
        {
            if (task == null || !task.IsCompleted) return;
            try
            {
                prepared = task.GetAwaiter().GetResult();
                message = prepared.AlreadyConfigured
                    ? "The Project already uses this exact PackageSet revision. No offline plan was exported."
                    : "Prepared. Review the immutable offline plan before applying. Project paths, index, Junctions and metadata were not changed.";
            }
            catch (Exception error)
            {
                message = error.Message;
                prepared = null;
            }
            finally
            {
                task = null;
                if (cancellation != null) cancellation.Dispose();
                cancellation = null;
                Repaint();
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("Package Manager exposes exactly two workflows. Create PackageSet accepts explicit Project source selections and writes only a new external PackageSet during Prepare. Use Existing PackageSet treats its manifest and gitlinks as read-only authority. Project cutover occurs only through the reviewed offline plan after Unity exits.", MessageType.Info);
            using (new EditorGUI.DisabledScope(task != null || prepared != null))
            {
                int selectedWorkflow = EditorGUILayout.Popup("Operation", (int)workflow, WorkflowLabels);
                workflow = (WorkflowKind)selectedWorkflow;
                DrawProjectConfiguration();
                DrawCheckoutSelection();
                if (workflow == WorkflowKind.CreatePackageSet) DrawCreateInputs();
                else DrawUseExistingInputs();
                bool prepareClicked = GUILayout.Button("Prepare");
                if (prepareClicked) RunGuarded(Prepare);
            }

            if (task != null)
            {
                EditorGUILayout.LabelField("Observing, validating and preparing external PackageSet state...");
                bool cancelClicked = GUILayout.Button("Cancel preparation");
                if (cancelClicked && cancellation != null) cancellation.Cancel();
            }

            if (prepared != null) DrawPreparation();
            if (message.Length != 0) EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawProjectConfiguration()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Current durable Project PackageSet binding", EditorStyles.boldLabel);
            string configuredRepository = projectConfiguration.repository ?? string.Empty;
            string configuredTrackingRef = projectConfiguration.trackingRef ?? string.Empty;
            string configuredRevision = projectConfiguration.resolvedRevision ?? string.Empty;
            string configuredProjectionRoot = projectConfiguration.managedProjectionRoot ?? string.Empty;
            DrawReadOnly("Repository", configuredRepository);
            DrawReadOnly("Tracking ref", configuredTrackingRef);
            string revision = configuredRevision;
            if (string.IsNullOrWhiteSpace(revision)) revision = "<not resolved yet>";
            DrawReadOnly("Resolved revision", revision);
            DrawReadOnly("Managed projection root", configuredProjectionRoot);
            bool settingsClicked = GUILayout.Button("View Project PackageSet Settings");
            if (settingsClicked) SettingsService.OpenProjectSettings(CraftyRacoonPackagesSettingsProvider.SettingsPath);
        }

        private void DrawCheckoutSelection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("External PackageSet checkout", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                packageSetRoot = EditorGUILayout.TextField("Checkout / destination", packageSetRoot);
                GUILayoutOption browseWidth = GUILayout.Width(70);
                bool browseClicked = GUILayout.Button("Browse", browseWidth);
                if (browseClicked)
                {
                    string selected = EditorUtility.OpenFolderPanel("Choose a PackageSet checkout or destination", packageSetRoot, string.Empty);
                    if (!string.IsNullOrWhiteSpace(selected)) packageSetRoot = selected;
                }
            }
        }

        private void DrawCreateInputs()
        {
            createRepository = EditorGUILayout.TextField("New PackageSet repository", createRepository);
            createTrackingRef = EditorGUILayout.TextField("Tracking ref", createTrackingRef);
            projectionRoot = EditorGUILayout.TextField("Managed projection root", projectionRoot);
            sourceRoot = EditorGUILayout.TextField("Canonical source root", sourceRoot);
            DirectoryInfo projectDirectory = new DirectoryInfo(ProjectRoot);
            string identity = "PackageSet." + projectDirectory.Name;
            DrawReadOnly("New PackageSet identity", identity);
            EditorGUILayout.HelpBox("Create PackageSet requires an absent or empty destination. Only the selections below can become manifest entries. Package Manager runtime is never included implicitly.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool readSubmodulesClicked = GUILayout.Button("Read Project submodules");
                if (readSubmodulesClicked) RunGuarded(ReadProjectSubmodules);
                bool includeRuntimeClicked = GUILayout.Button("Include Package Manager Runtime");
                if (includeRuntimeClicked) RunGuarded(IncludePackageManagerRuntime);
                bool addLocalSourceClicked = GUILayout.Button("Add local source folder");
                if (addLocalSourceClicked) RunGuarded(AddLocalSource);
            }
            foreach (Candidate candidate in candidates)
            {
                EditorGUILayout.BeginVertical("box");
                candidate.Selected = EditorGUILayout.ToggleLeft(candidate.ProjectSourcePath + " (" + candidate.Kind + ")", candidate.Selected);
                candidate.Identity = EditorGUILayout.TextField("Canonical identity", candidate.Identity);
                string sourcePath = DisplaySourcePath(candidate.Identity);
                string projectionPath = DisplayProjectionPath(candidate.Identity);
                EditorGUILayout.LabelField("Source: " + sourcePath);
                EditorGUILayout.LabelField("Projection: " + projectionPath);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.HelpBox("Select reusable sources only. LocalDraft creates a local committed baseline and remains NeedsPublish until it is converted to a portable remote-backed source.", MessageType.Warning);
        }

        private void DrawUseExistingInputs()
        {
            int selectedStrategy = EditorGUILayout.Popup("Acquisition", (int)acquisitionStrategy, AcquisitionLabels);
            acquisitionStrategy = (PackageSetAcquisitionStrategy)selectedStrategy;
            repository = EditorGUILayout.TextField("PackageSet repository", repository);
            if (acquisitionStrategy == PackageSetAcquisitionStrategy.OpenLocalCheckout)
                EditorGUILayout.HelpBox("The local checkout HEAD is used as-is and must match the Project exact revision when one is recorded. Declared source checkouts may be initialized at existing gitlinks; manifest and gitlinks remain unchanged.", MessageType.None);
            else if (acquisitionStrategy == PackageSetAcquisitionStrategy.CloneOrOpenExactRevision)
            {
                resolvedRevision = EditorGUILayout.TextField("Exact revision", resolvedRevision);
                EditorGUILayout.HelpBox("The transient repository input is cloned or opened at the immutable exact revision. It is persisted only by successful offline Apply.", MessageType.None);
            }
            else
            {
                trackingRef = EditorGUILayout.TextField("Tracking ref", trackingRef);
                EditorGUILayout.HelpBox("The transient tracking ref is resolved once to an immutable revision. Apply records the resolved revision only after review.", MessageType.None);
            }
            EditorGUILayout.HelpBox("Use Existing PackageSet has no Project source selections. Every planned entry comes from the selected PackageSet manifest and gitlinks, and all Project changes remain deferred to offline Apply.", MessageType.Warning);
        }

        private void DrawPreparation()
        {
            if (prepared == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Exact PackageSet revision", prepared.ExactRevision);
            foreach (PackageSetEntryPlan entry in prepared.Entries)
            {
                string summary = entry.Placement.Identity + "\n" + entry.Placement.OriginalProjectPath + " -> " + entry.Placement.ProjectionPath;
                summary += "\n" + entry.Observation.ProjectionState + " / " + entry.Assessment.Decision;
                if (!string.IsNullOrWhiteSpace(entry.SourceRepository))
                    summary += "\nSource repository: " + entry.SourceRepository;
                if (entry.Assessment.NeedsPublish) summary += " / NeedsPublish";
                EditorGUILayout.HelpBox(summary, MessageType.None);
            }
            if (prepared.AlreadyConfigured)
            {
                EditorGUILayout.HelpBox("AlreadyConfigured is a read-only no-op. Prepare did not export an apply plan.", MessageType.Info);
            }
            else
            {
                bool showPlanClicked = GUILayout.Button("Show offline apply plan");
                if (showPlanClicked) EditorUtility.RevealInFinder(prepared.ApplyInstructionsPath);
                using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || EditorApplication.isUpdating))
                {
                    bool applyClicked = GUILayout.Button("Apply, close Unity, then reopen");
                    if (applyClicked) RunGuarded(ApplyAndReopen);
                }
            }
            bool discardClicked = GUILayout.Button("Discard preview (retain external checkout and operation evidence)");
            if (discardClicked) prepared = null;
        }

        private void RunGuarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                message = error.Message;
            }
        }

        private void ReadProjectSubmodules()
        {
            string gitRoot = IO.Git(ProjectRoot, "rev-parse", "--show-toplevel");
            string config = Path.Combine(gitRoot, ".gitmodules");
            if (!File.Exists(config))
            {
                message = "Project has no declared submodules.";
                return;
            }
            string values = IO.Git(gitRoot, "config", "--file", config, "--null", "--list");
            string[] entries = values.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                int split = entry.IndexOf('\n');
                if (split < 0) continue;
                string key = entry.Substring(0, split);
                bool submoduleKey = key.StartsWith("submodule.", StringComparison.Ordinal);
                bool pathKey = key.EndsWith(".path", StringComparison.Ordinal);
                if (!submoduleKey || !pathKey) continue;
                string relative = entry.Substring(split + 1);
                string full = IO.Under(gitRoot, relative);
                if (!IO.Within(Application.dataPath, full)) continue;
                DirectoryInfo sourceDirectory = new DirectoryInfo(full);
                string projectSourcePath = IO.Relative(ProjectRoot, full);
                AddCandidate(sourceDirectory.Name, projectSourcePath, DevelopmentSourceKind.GitSubmodule, false);
            }
        }

        private void IncludePackageManagerRuntime()
        {
            PackageManagementSourceComposition composition = PackageManagementSourceComposition.Discover();
            PackageManagementSourceComposition.SourceBinding[] sources =
            {
                composition.Module,
                composition.Framework,
                composition.UnityFramework
            };
            foreach (PackageManagementSourceComposition.SourceBinding source in sources)
            {
                if (!source.IsReady)
                {
                    string diagnosticText = string.Join(" | ", source.Diagnostics);
                    throw new InvalidOperationException(source.Id + ": " + diagnosticText);
                }
                string projectSourcePath = IO.Relative(ProjectRoot, source.AbsolutePath);
                AddCandidate(source.Id, projectSourcePath, DevelopmentSourceKind.GitSubmodule, true);
            }
            if (composition.IsSelfObserved)
                message = "Package Manager runtime was self-observed from the assemblies currently compiled by Unity. Inclusion remains an explicit Create PackageSet selection.";
        }

        private void AddLocalSource()
        {
            string folder = EditorUtility.OpenFolderPanel("Select reusable source to extract", Application.dataPath, string.Empty);
            if (folder.Length == 0) return;
            if (!IO.Within(Application.dataPath, folder)) throw new InvalidOperationException("Choose an explicit child of this Project's Assets.");
            DirectoryInfo sourceDirectory = new DirectoryInfo(folder);
            string projectSourcePath = IO.Relative(ProjectRoot, folder);
            AddCandidate(sourceDirectory.Name, projectSourcePath, DevelopmentSourceKind.LocalDraft, true);
        }

        private void AddCandidate(string identity, string projectSourcePath, DevelopmentSourceKind kind, bool selected)
        {
            foreach (Candidate candidate in candidates)
            {
                if (!candidate.ProjectSourcePath.Equals(projectSourcePath, StringComparison.OrdinalIgnoreCase)) continue;
                candidate.Selected = candidate.Selected || selected;
                return;
            }
            Candidate added = new Candidate();
            added.Selected = selected;
            added.Identity = identity;
            added.ProjectSourcePath = projectSourcePath;
            added.Kind = kind;
            candidates.Add(added);
        }

        private void Prepare()
        {
            bool checkoutMissing = string.IsNullOrWhiteSpace(packageSetRoot);
            if (checkoutMissing) throw new InvalidOperationException("Choose a PackageSet checkout or destination.");
            PackageManagementSourceComposition composition = PackageManagementSourceComposition.Discover();
            string unityFrameworkPath = composition.UnityFramework.AbsolutePath;
            string helper = Path.Combine(unityFrameworkPath, "Tools/ApplyDevelopmentBootstrap.ps1");
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            PreparationWorkItem workItem;
            string projectRoot = ProjectRoot;
            if (workflow == WorkflowKind.CreatePackageSet)
            {
                List<CreatePackageSetSelection> selections = BuildCreateSelections();
                DirectoryInfo projectDirectory = new DirectoryInfo(projectRoot);
                string identity = "PackageSet." + projectDirectory.Name;
                CreatePackageSetRequest request = new CreatePackageSetRequest(projectRoot, packageSetRoot, identity, createRepository, createTrackingRef, sourceRoot, projectionRoot, selections);
                workItem = new PreparationWorkItem(request, helper, EditorApplication.applicationPath, token);
            }
            else
            {
                string exactRevision = resolvedRevision;
                if (acquisitionStrategy == PackageSetAcquisitionStrategy.ResolveTrackingRefOnce) exactRevision = string.Empty;
                UseExistingPackageSetRequest request = new UseExistingPackageSetRequest(projectRoot, packageSetRoot, repository, trackingRef, exactRevision, acquisitionStrategy);
                workItem = new PreparationWorkItem(request, helper, EditorApplication.applicationPath, token);
            }
            task = Task.Factory.StartNew(workItem.Execute, token, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            message = string.Empty;
        }

        private List<CreatePackageSetSelection> BuildCreateSelections()
        {
            List<CreatePackageSetSelection> selections = new List<CreatePackageSetSelection>();
            foreach (Candidate candidate in candidates)
            {
                if (!candidate.Selected) continue;
                bool runtime = WindowsPackageSetEnvironmentSupport.IsRuntimeIdentity(candidate.Identity);
                CreatePackageSetSelection selection = new CreatePackageSetSelection(candidate.Identity, candidate.ProjectSourcePath, candidate.Kind, runtime);
                selections.Add(selection);
            }
            return selections;
        }

        private void LoadConfiguration()
        {
            LoadProjectConfiguration();
            LocalPackageSetStateDocument local = PackageSetProjectConfiguration.LoadLocal(ProjectRoot);
            if (!string.IsNullOrWhiteSpace(local.checkoutPath)) packageSetRoot = local.checkoutPath;
            if (acquisitionInitialized) return;
            if (!string.IsNullOrWhiteSpace(local.checkoutPath)) acquisitionStrategy = PackageSetAcquisitionStrategy.OpenLocalCheckout;
            else if (!string.IsNullOrWhiteSpace(resolvedRevision)) acquisitionStrategy = PackageSetAcquisitionStrategy.CloneOrOpenExactRevision;
            else acquisitionStrategy = PackageSetAcquisitionStrategy.ResolveTrackingRefOnce;
            acquisitionInitialized = true;
        }

        private void LoadProjectConfiguration()
        {
            projectConfiguration = PackageSetProjectConfiguration.LoadProject(ProjectRoot);
            repository = projectConfiguration.repository ?? string.Empty;
            trackingRef = projectConfiguration.trackingRef;
            if (string.IsNullOrWhiteSpace(trackingRef)) trackingRef = "main";
            resolvedRevision = projectConfiguration.resolvedRevision ?? string.Empty;
            projectionRoot = projectConfiguration.managedProjectionRoot;
            if (string.IsNullOrWhiteSpace(projectionRoot)) projectionRoot = "Assets/CraftyRacoon/Managed";
        }

        private string DisplaySourcePath(string canonicalIdentity)
        {
            try
            {
                return PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath(sourceRoot, canonicalIdentity);
            }
            catch (Exception error)
            {
                return "<invalid: " + error.Message + ">";
            }
        }

        private string DisplayProjectionPath(string canonicalIdentity)
        {
            try
            {
                return PackageSourceDirectoryPolicy.ResolveManagedProjectionPath(projectionRoot, canonicalIdentity);
            }
            catch (Exception error)
            {
                return "<invalid: " + error.Message + ">";
            }
        }

        private static void DrawReadOnly(string label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(label, value ?? string.Empty);
            }
        }

        private void ApplyAndReopen()
        {
            if (prepared == null || string.IsNullOrWhiteSpace(prepared.ApplyInstructionsPath)) return;
            bool approved = EditorUtility.DisplayDialog("Apply PackageSet conversion", "Unity must close before the reviewed offline transaction changes Project paths, metadata and index. Continue?", "Save, close and apply", "Cancel");
            if (!approved) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.SaveAssets();
            string plan = prepared.ApplyInstructionsPath;
            string? operation = Path.GetDirectoryName(plan);
            bool operationMissing = string.IsNullOrWhiteSpace(operation);
            if (operationMissing) throw new InvalidOperationException("Offline operation directory is unavailable.");
            string operationPath = operation!;
            string helper = Path.Combine(operationPath, "ApplyDevelopmentBootstrap.ps1");
            string helperHashPath = Path.Combine(operationPath, "helper.sha256");
            string helperHashContents = File.ReadAllText(helperHashPath);
            string expectedHelperHash = helperHashContents.Trim();
            string actualHelperHash = IO.HashFile(helper);
            if (!actualHelperHash.Equals(expectedHelperHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Offline helper changed after preparation.");
            string quotedHelper = IO.Quote(helper);
            string quotedPlan = IO.Quote(plan);
            string arguments = "-NoProfile -ExecutionPolicy Bypass -File " + quotedHelper + " -Plan " + quotedPlan;
            System.Diagnostics.Process currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            int processId = currentProcess.Id;
            currentProcess.Dispose();
            arguments += " -WaitForProcessId " + processId + " -ReopenUnity";
            System.Diagnostics.ProcessStartInfo start = new System.Diagnostics.ProcessStartInfo("powershell.exe", arguments);
            start.UseShellExecute = true;
            System.Diagnostics.Process.Start(start);
            EditorApplication.Exit(0);
        }
    }
}
