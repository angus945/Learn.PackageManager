using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CraftyRacoon.PackageManagement.Development;
using CraftyRacoon.PackageManagement.Framework;
using CraftyRacoon.PackageManagement.PackageSets;
using CraftyRacoon.PackageManagement.Unity3D.Editor.Development;
using UnityEditor.Compilation;
using UnityEngine;
using IO = CraftyRacoon.PackageManagement.Unity3D.Editor.Development.DevelopmentBootstrapIO;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor
{
    internal sealed class PackageManagementSourceComposition
    {
        internal const string ModuleId = "Module.PackageManagement";
        internal const string FrameworkId = "Framework.PackageManagement";
        internal const string UnityFrameworkId = "Framework.PackageManagement.Unity3D";

        private const string ModuleExpected =
            "src/Module.PackageManagement/Module.PackageManagement.csproj";
        private const string FrameworkExpected =
            "src/Framework.PackageManagement/Framework.PackageManagement.csproj";
        private const string UnityFrameworkExpected =
            "Editor/Framework.PackageManagement.Unity3D.Editor.asmdef";

        private PackageManagementSourceComposition(
            string unityRoot,
            string gitRoot,
            string bindingsPath,
            BootstrapPlanDocument handoff,
            bool managed,
            bool selfObserved,
            SourceBinding module,
            SourceBinding framework,
            SourceBinding unity)
        {
            UnityProjectRoot = unityRoot;
            GitRoot = gitRoot;
            HandoffPath = bindingsPath;
            Handoff = handoff;
            IsPackageSetManaged = managed;
            IsSelfObserved = selfObserved;
            Module = module;
            Framework = framework;
            UnityFramework = unity;
            ModuleAssemblyName =
                typeof(PackageSetBinding).Assembly.GetName().Name ?? ModuleId;
            FrameworkAssemblyName =
                typeof(PackageManagementFramework).Assembly.GetName().Name ?? FrameworkId;
        }

        internal string UnityProjectRoot { get; }
        internal string GitRoot { get; }
        internal string HandoffPath { get; }
        internal BootstrapPlanDocument Handoff { get; }
        internal bool IsPackageSetManaged { get; }
        internal bool IsSelfObserved { get; }
        internal SourceBinding Module { get; }
        internal SourceBinding Framework { get; }
        internal SourceBinding UnityFramework { get; }
        internal string ModuleAssemblyName { get; }
        internal string FrameworkAssemblyName { get; }
        internal bool SourceCompositionReady =>
            Module.IsReady && Framework.IsReady && UnityFramework.IsReady;
        internal bool TypedFrameworkAvailable =>
            typeof(PackageManagementFramework) != null &&
            typeof(PackageSetBinding) != null;

        internal sealed class SourceBinding
        {
            internal SourceBinding(
                string id,
                string repository,
                string revision,
                string relativePath,
                string absolutePath,
                string expectedPath,
                IReadOnlyList<string> diagnostics)
            {
                Id = id;
                Repository = repository;
                Revision = revision;
                RelativePath = relativePath;
                AbsolutePath = absolutePath;
                ExpectedProjectPath = expectedPath;
                Diagnostics = diagnostics;
            }

            internal string Id { get; }
            internal string Repository { get; }
            internal string Revision { get; }
            internal string RelativePath { get; }
            internal string AbsolutePath { get; }
            internal string ExpectedProjectPath { get; }
            internal IReadOnlyList<string> Diagnostics { get; }
            internal bool IsReady => Diagnostics.Count == 0;
        }

        internal static PackageManagementSourceComposition Discover()
        {
            string unityRoot =
                Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string gitRoot = FindGitRoot(unityRoot);

            string managedPath =
                Path.Combine(gitRoot, "PackageManagement", "runtime.bindings.json");
            bool managedPathOccupied =
                WindowsPackageSetEnvironmentSupport.DurablePathOccupied(managedPath);
            if (managedPathOccupied)
                return ReadManaged(unityRoot, gitRoot, managedPath);

            string bootstrapPath =
                Path.Combine(gitRoot, "PackageManagement", "bootstrap.plan.json");
            bool bootstrapPathOccupied =
                WindowsPackageSetEnvironmentSupport.DurablePathOccupied(bootstrapPath);
            if (bootstrapPathOccupied)
                return ReadBootstrap(unityRoot, gitRoot, bootstrapPath);

            return ObserveCurrentRuntime(unityRoot, gitRoot);
        }

        private static PackageManagementSourceComposition ReadManaged(
            string unityRoot,
            string gitRoot,
            string path)
        {
            string json = ReadDurableHandoff(path, "PackageSet-managed runtime bindings");
            List<string> diagnostics = new List<string>();
            bool validShape = StrictBindingJsonShape.ValidateRuntime(json, diagnostics);
            if (!validShape)
            {
                string[] diagnosticItems = diagnostics.ToArray();
                string diagnosticText = string.Join("; ", diagnosticItems);
                string message = "Invalid PackageSet-managed runtime bindings JSON shape: " + diagnosticText;
                throw new InvalidOperationException(message);
            }
            RuntimeBindingsDocument bindings =
                JsonUtility.FromJson<RuntimeBindingsDocument>(json);
            if (bindings == null ||
                bindings.schemaVersion != 1 ||
                bindings.components == null ||
                bindings.components.Length != 3)
            {
                throw new InvalidOperationException(
                    "Invalid PackageSet-managed runtime bindings. " +
                    "Bootstrap/self-observation fallback is not allowed.");
            }

            var document = new BootstrapPlanDocument
            {
                schemaVersion = 2,
                planId = "PackageSet-managed",
                components = bindings.components.Select(
                    component => new BootstrapPlanComponent
                    {
                        id = component.id,
                        repository = component.repository,
                        path = component.path
                    }).ToArray()
            };

            ValidateComponentSet(document);
            return Compose(
                unityRoot,
                gitRoot,
                path,
                document,
                managed: true,
                selfObserved: false);
        }

        private static PackageManagementSourceComposition ReadBootstrap(
            string unityRoot,
            string gitRoot,
            string path)
        {
            string json = ReadDurableHandoff(path, "bootstrap handoff");
            StrictJsonValue strictDocument = StrictJsonDocument.Parse(json);
            if (strictDocument.Kind != StrictJsonValueKind.Object)
                throw new InvalidOperationException("Bootstrap handoff must be a JSON object.");
            BootstrapPlanDocument document =
                JsonUtility.FromJson<BootstrapPlanDocument>(json);
            if (document == null ||
                document.schemaVersion != 2 ||
                document.bootstrapper != "Tool.PackageManager" ||
                document.components == null)
            {
                throw new InvalidOperationException(
                    "Unsupported bootstrap handoff. Self-observation is not used " +
                    "when an explicit handoff exists but is invalid.");
            }

            ValidateComponentSet(document);
            return Compose(
                unityRoot,
                gitRoot,
                path,
                document,
                managed: false,
                selfObserved: false);
        }

        private static string ReadDurableHandoff(string path, string description)
        {
            bool regularFile = WindowsPackageSetEnvironmentSupport.IsRegularDurableFile(path);
            string invalidFileMessage = description + " must be an ordinary durable file: " + path;
            if (!regularFile) throw new InvalidOperationException(invalidFileMessage);
            WindowsPackageSetEnvironmentSupport.DurableFileSnapshot snapshot = WindowsPackageSetEnvironmentSupport.OpenDurableFileSnapshot(path);
            using (snapshot)
            {
                string text = snapshot.Text;
                bool hasEncodingSignature = text.Length > 0 && text[0] == '\uFEFF';
                if (hasEncodingSignature) text = text.Substring(1);
                return text;
            }
        }

        private static PackageManagementSourceComposition ObserveCurrentRuntime(
            string unityRoot,
            string gitRoot)
        {
            string moduleAssembly =
                typeof(PackageSetBinding).Assembly.GetName().Name ?? ModuleId;
            string frameworkAssembly =
                typeof(PackageManagementFramework).Assembly.GetName().Name ?? FrameworkId;
            string unityAssembly =
                typeof(PackageManagementSourceComposition).Assembly.GetName().Name ??
                "Framework.PackageManagement.Unity3D.Editor";

            BootstrapPlanComponent module =
                ObserveComponent(
                    gitRoot,
                    unityRoot,
                    ModuleId,
                    moduleAssembly,
                    ModuleExpected);
            BootstrapPlanComponent framework =
                ObserveComponent(
                    gitRoot,
                    unityRoot,
                    FrameworkId,
                    frameworkAssembly,
                    FrameworkExpected);
            BootstrapPlanComponent unity =
                ObserveComponent(
                    gitRoot,
                    unityRoot,
                    UnityFrameworkId,
                    unityAssembly,
                    UnityFrameworkExpected);

            var document = new BootstrapPlanDocument
            {
                schemaVersion = 2,
                bootstrapper = "SelfObservedRuntime",
                planId = "Self-observed runtime",
                components = new[] { module, framework, unity }
            };

            ValidateComponentSet(document);
            return Compose(
                unityRoot,
                gitRoot,
                string.Empty,
                document,
                managed: false,
                selfObserved: true);
        }

        private static BootstrapPlanComponent ObserveComponent(
            string gitRoot,
            string unityRoot,
            string id,
            string assemblyName,
            string marker)
        {
            UnityEditor.Compilation.Assembly[] assemblies =
                CompilationPipeline.GetAssemblies()
                    .Where(assembly =>
                        assembly.name.Equals(
                            assemblyName,
                            StringComparison.Ordinal))
                    .ToArray();

            if (assemblies.Length != 1)
            {
                throw new InvalidOperationException(
                    id + ": expected exactly one compiled Unity assembly named '" +
                    assemblyName + "' but observed " + assemblies.Length + ".");
            }

            StringComparer comparer =
                Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal;

            string assetsRoot =
                Path.GetFullPath(Path.Combine(unityRoot, "Assets"));
            string[] roots = assemblies[0].sourceFiles
                .Select(source => ResolveSourceFile(unityRoot, source))
                .Select(source => FindOwningRoot(source, assetsRoot, marker))
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(comparer)
                .ToArray();

            if (roots.Length != 1)
            {
                throw new InvalidOperationException(
                    id + ": current assembly source ownership is ambiguous. " +
                    "Expected one runtime root containing '" + marker +
                    "', observed " + roots.Length + ".");
            }

            string root = roots[0];
            if (!IsWithin(assetsRoot, root))
            {
                throw new InvalidOperationException(
                    id + ": self-observed runtime is outside this Unity Assets tree.");
            }

            if (!IO.Full(IO.Git(root, "rev-parse", "--show-toplevel"))
                    .Equals(IO.Full(root), PathComparison))
            {
                throw new InvalidOperationException(
                    id + ": self-observed runtime must be an independent Git checkout.");
            }

            string repository = IO.Git(root, "remote", "get-url", "origin");
            string revision = IO.Git(root, "rev-parse", "HEAD");
            string relative = IO.Relative(gitRoot, root);

            return new BootstrapPlanComponent
            {
                id = id,
                repository = repository,
                revision = revision,
                path = relative
            };
        }

        private static string ResolveSourceFile(string unityRoot, string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return string.Empty;

            return Path.GetFullPath(
                Path.IsPathRooted(source)
                    ? source
                    : Path.Combine(unityRoot, source));
        }

        private static string FindOwningRoot(
            string sourceFile,
            string assetsRoot,
            string marker)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
                return string.Empty;

            DirectoryInfo? current =
                new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? sourceFile);
            while (current != null && IsWithinOrSame(assetsRoot, current.FullName))
            {
                string candidate = Path.Combine(
                    current.FullName,
                    marker.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    return Path.GetFullPath(current.FullName);

                current = current.Parent;
            }

            return string.Empty;
        }

        private static PackageManagementSourceComposition Compose(
            string unityRoot,
            string gitRoot,
            string path,
            BootstrapPlanDocument document,
            bool managed,
            bool selfObserved)
        {
            return new PackageManagementSourceComposition(
                unityRoot,
                gitRoot,
                path,
                document,
                managed,
                selfObserved,
                Resolve(
                    gitRoot,
                    unityRoot,
                    document,
                    ModuleId,
                    ModuleExpected,
                    managed),
                Resolve(
                    gitRoot,
                    unityRoot,
                    document,
                    FrameworkId,
                    FrameworkExpected,
                    managed),
                Resolve(
                    gitRoot,
                    unityRoot,
                    document,
                    UnityFrameworkId,
                    UnityFrameworkExpected,
                    managed));
        }

        private static void ValidateComponentSet(BootstrapPlanDocument document)
        {
            if (document.components == null ||
                document.components.Any(component => component == null) ||
                document.components
                    .Select(component => component.id)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != document.components.Length)
            {
                throw new InvalidOperationException(
                    "Duplicate or missing runtime component identity.");
            }
        }

        private static SourceBinding Resolve(
            string gitRoot,
            string unityRoot,
            BootstrapPlanDocument document,
            string id,
            string expected,
            bool managed)
        {
            BootstrapPlanComponent? component =
                document.components.FirstOrDefault(candidate => candidate.id == id);
            if (component == null)
            {
                return new SourceBinding(
                    id,
                    "",
                    "",
                    "",
                    "",
                    "",
                    new[] { "Component is missing from runtime bindings." });
            }

            var diagnostics = new List<string>();
            if (string.IsNullOrWhiteSpace(component.repository))
                diagnostics.Add("Repository identity is empty.");
            if (!managed && string.IsNullOrWhiteSpace(component.revision))
                diagnostics.Add("Runtime revision evidence is empty.");

            string relative = PackageRelativePath.Normalize(component.path);
            string absolute =
                Path.GetFullPath(Path.Combine(gitRoot, relative));
            string assets =
                Path.GetFullPath(Path.Combine(unityRoot, "Assets")) +
                Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(assets, PathComparison))
            {
                diagnostics.Add(
                    "Runtime projection must remain within this Unity project's Assets tree.");
            }

            if (!Directory.Exists(absolute))
                diagnostics.Add("Runtime source/projection directory does not exist.");

            string entry = Path.Combine(
                absolute,
                expected.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(entry) && !Directory.Exists(entry))
                diagnostics.Add("Expected source entry is missing: " + expected);

            return new SourceBinding(
                id,
                component.repository,
                managed ? string.Empty : component.revision,
                relative,
                absolute,
                entry,
                diagnostics);
        }

        private static StringComparison PathComparison =>
            Environment.OSVersion.Platform == PlatformID.Win32NT
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static bool IsWithin(string parent, string child)
        {
            string root =
                Path.GetFullPath(parent)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(child);
            return candidate.StartsWith(root, PathComparison);
        }

        private static bool IsWithinOrSame(string parent, string child)
        {
            string first = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string second = Path.GetFullPath(child)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return second.Equals(first, PathComparison) ||
                   second.StartsWith(first + Path.DirectorySeparatorChar, PathComparison);
        }

        private static string FindGitRoot(string start)
        {
            DirectoryInfo? current = new DirectoryInfo(start);
            while (current != null)
            {
                string git = Path.Combine(current.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                    return current.FullName;

                current = current.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the enclosing Project Git root.");
        }
    }
}
