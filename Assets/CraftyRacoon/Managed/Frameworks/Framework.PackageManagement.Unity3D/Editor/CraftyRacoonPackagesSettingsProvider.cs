using System;
using System.IO;
using CraftyRacoon.PackageManagement.Unity3D.Editor.Development;
using UnityEditor;
using UnityEngine;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor
{
    internal static class CraftyRacoonPackagesSettingsProvider
    {
        internal const string SettingsPath = "Project/CraftyRacoon Packages";

        private static PackageManagementCompositionStatus status = null!;
        private static ProjectPackageSetDocument projectConfiguration = null!;
        private static LocalPackageSetStateDocument localConfiguration = null!;
        private static string configurationMessage = string.Empty;

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "CraftyRacoon Packages",
                keywords = new[]
                {
                    "CraftyRacoon", "Packages", "Package Management",
                    "Bootstrap", "Submodule", "PackageSet", "Development"
                },
                guiHandler = DrawGUI
            };
        }

        private static string ProjectRoot
        {
            get
            {
                string parent = Path.Combine(Application.dataPath, "..");
                return Path.GetFullPath(parent);
            }
        }

        private static void DrawGUI(string searchContext)
        {
            if (status == null) Refresh();
            if (projectConfiguration == null || localConfiguration == null)
                RefreshConfiguration();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("CraftyRacoon Packages", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                {
                    Refresh();
                    RefreshConfiguration();
                }
            }

            DrawProjectConfiguration();
            EditorGUILayout.Space();
            DrawLocalConfiguration();

            EditorGUILayout.Space();
            if (GUILayout.Button("Open Create / Use Existing PackageSet Workflows"))
                DevelopmentBootstrapWindow.Open();

            if (!string.IsNullOrWhiteSpace(configurationMessage))
                EditorGUILayout.HelpBox(configurationMessage, MessageType.Info);

            EditorGUILayout.Space();
            if (status == null)
            {
                EditorGUILayout.HelpBox("Status has not been observed.", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                status.Summary,
                status.Succeeded ? MessageType.Info : MessageType.Warning);

            PackageManagementSourceComposition? composition = status.Composition;
            if (composition != null)
            {
                DrawValue("Unity project root", composition.UnityProjectRoot);
                DrawValue("Project Git root", composition.GitRoot);
                string runtimeResolution = DescribeRuntimeResolution(composition);
                string durableBindingSource = DescribeDurableBindingSource(composition);
                string runtimeComposition = DescribeRuntimeComposition(composition);
                DrawValue("Runtime resolution", runtimeResolution);
                DrawValue("Durable binding source", durableBindingSource);
                DrawValue("Composition", runtimeComposition);
                if (!composition.IsPackageSetManaged && !composition.IsSelfObserved)
                    DrawValue("Initial source root", composition.Handoff.initialSourceRoot);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Package Management runtime", EditorStyles.boldLabel);
                DrawBinding(composition.Module, composition.IsPackageSetManaged);
                DrawBinding(composition.Framework, composition.IsPackageSetManaged);
                DrawBinding(composition.UnityFramework, composition.IsPackageSetManaged);

                EditorGUILayout.Space();
                DrawValue("Module assembly", composition.ModuleAssemblyName);
                DrawValue("Framework assembly", composition.FrameworkAssemblyName);
                string frameworkAvailability = composition.TypedFrameworkAvailable ? "Ready" : "Not ready";
                EditorGUILayout.LabelField("Typed Framework available", frameworkAvailability);
                string runtimeAuthorityMessage = DescribeRuntimeAuthority(composition);
                EditorGUILayout.HelpBox(runtimeAuthorityMessage, MessageType.None);
            }

            foreach (string diagnostic in status.Diagnostics)
                EditorGUILayout.HelpBox(diagnostic, MessageType.None);
        }

        private static void DrawProjectConfiguration()
        {
            EditorGUILayout.LabelField("Project PackageSet", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Read-only shared authority from PackageManagement/project.packageset.json. Create PackageSet or Use Existing PackageSet may change it only through a reviewed offline transaction.", MessageType.None);

            DrawValue("PackageSet repository", projectConfiguration.repository);
            DrawValue("Tracking ref", projectConfiguration.trackingRef);
            string resolvedRevision = string.IsNullOrWhiteSpace(projectConfiguration.resolvedRevision) ? "<not resolved yet>" : projectConfiguration.resolvedRevision;
            DrawValue("Resolved revision", resolvedRevision);
            EditorGUILayout.HelpBox("Tracking ref may be resolved once only when no exact revision exists. A successful reviewed initialization records the immutable resolved revision; later updates require a separate workflow.", MessageType.None);
            DrawValue("Managed projection root", projectConfiguration.managedProjectionRoot);

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.Toggle("Needs publish", projectConfiguration.needsPublish);

        }

        private static void DrawLocalConfiguration()
        {
            EditorGUILayout.LabelField("Local Development", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Read-only per-working-copy state written only after a successful offline Apply. Checkout selection belongs to Use Existing PackageSet.", MessageType.None);
            DrawValue("PackageSet checkout", localConfiguration.checkoutPath);
        }

        private static void Refresh()
        {
            status = PackageManagementCompositionStatus.Observe();
        }

        private static void RefreshConfiguration()
        {
            projectConfiguration = new ProjectPackageSetDocument();
            localConfiguration = new LocalPackageSetStateDocument();
            RunConfiguration(LoadConfiguration);
        }

        private static void LoadConfiguration()
        {
            projectConfiguration = PackageSetProjectConfiguration.LoadProject(ProjectRoot);
            localConfiguration = PackageSetProjectConfiguration.LoadLocal(ProjectRoot);
        }

        private static void RunConfiguration(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                configurationMessage = exception.Message;
            }
        }

        private static string DescribeRuntimeResolution(PackageManagementSourceComposition composition)
        {
            if (composition.IsPackageSetManaged) return "PackageSet-managed runtime bindings";
            if (composition.IsSelfObserved) return "Self-observed from compiled Unity assemblies";
            return "Tool bootstrap handoff";
        }

        private static string DescribeDurableBindingSource(PackageManagementSourceComposition composition)
        {
            if (composition.IsSelfObserved) return "<none; transient observation>";
            return composition.HandoffPath;
        }

        private static string DescribeRuntimeComposition(PackageManagementSourceComposition composition)
        {
            if (composition.IsPackageSetManaged) return "PackageSet-managed projection";
            if (composition.IsSelfObserved) return "Self-observed runtime";
            return composition.Handoff.planId;
        }

        private static string DescribeRuntimeAuthority(PackageManagementSourceComposition composition)
        {
            if (composition.IsPackageSetManaged)
                return "Runtime paths come from runtime.bindings.json. PackageSet gitlinks remain the exact revision authority.";
            if (composition.IsSelfObserved)
                return "No durable runtime binding file exists. Runtime ownership was recovered from the Package Manager assemblies currently compiled by Unity and is transient initialization evidence.";
            return "Runtime paths and bootstrap revisions come from bootstrap.plan.json. This handoff describes the initially installed Package Manager runtime.";
        }

        private static void DrawBinding(PackageManagementSourceComposition.SourceBinding binding, bool managed)
        {
            EditorGUILayout.LabelField(binding.Id, EditorStyles.boldLabel);
            DrawValue("Repository", binding.Repository);
            DrawValue(
                "Revision authority",
                managed ? "PackageSet gitlink (not re-pinned here)" : binding.Revision);
            DrawValue("Project path", binding.RelativePath);
            DrawValue("Resolved path", binding.AbsolutePath);
            EditorGUILayout.LabelField("Source ready", binding.IsReady ? "Ready" : "Not ready");
        }

        private static void DrawValue(string label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField(label, value ?? string.Empty);
        }
    }
}
