using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using CraftyRacoon.PackageManagement.Framework.Development;
using UnityEditor;
using UnityEngine;
using IO = CraftyRacoon.PackageManagement.Unity3D.Editor.Development.DevelopmentBootstrapIO;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    public static class UseExistingPackageSetPrepareCommand
    {
        [Serializable]
        private sealed class EntrySummary
        {
            public string identity = string.Empty;
            public string originalProjectPath = string.Empty;
            public string projectionPath = string.Empty;
            public string sourceRepository = string.Empty;
            public string observedRepository = string.Empty;
            public string sourceHead = string.Empty;
            public string exactPin = string.Empty;
            public string projectionState = string.Empty;
            public string decision = string.Empty;
            public bool needsPublish;
        }

        [Serializable]
        private sealed class PreparationSummary
        {
            public int schemaVersion = 1;
            public string operationPath = string.Empty;
            public string planPath = string.Empty;
            public string exactRevision = string.Empty;
            public bool alreadyConfigured;
            public EntrySummary[] entries = Array.Empty<EntrySummary>();
        }

        public static void Run()
        {
            try
            {
                RunCore();
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void RunCore()
        {
            bool batchMode = Application.isBatchMode;
            if (!batchMode) throw new InvalidOperationException("Use Existing Prepare command is available only in Unity batchmode.");
            string packageSetRoot = ReadRequiredAbsolutePath("--pm-package-set-root");
            string outputPath = ReadRequiredAbsolutePath("--pm-prepare-output");
            string projectParent = Path.Combine(Application.dataPath, "..");
            string projectRoot = IO.Full(projectParent);
            RequirePrivateOutputPath(projectRoot, packageSetRoot, outputPath);
            ProjectPackageSetDocument configuration = PackageSetProjectConfiguration.LoadProject(projectRoot);
            if (string.IsNullOrWhiteSpace(configuration.repository))
                throw new InvalidOperationException("Project PackageSet repository authority is missing.");
            if (string.IsNullOrWhiteSpace(configuration.resolvedRevision))
                throw new InvalidOperationException("Project PackageSet exact revision authority is missing.");

            PackageManagementSourceComposition composition = PackageManagementSourceComposition.Discover();
            string unityFrameworkPath = composition.UnityFramework.AbsolutePath;
            string helperPath = Path.Combine(unityFrameworkPath, "Tools/ApplyDevelopmentBootstrap.ps1");
            UseExistingPackageSetRequest request = new UseExistingPackageSetRequest(projectRoot, packageSetRoot, configuration.repository, configuration.trackingRef, configuration.resolvedRevision, PackageSetAcquisitionStrategy.OpenLocalCheckout);
            WindowsUseExistingPackageSetEnvironment environment = new WindowsUseExistingPackageSetEnvironment(helperPath, EditorApplication.applicationPath);
            IProjectPackageSetConverter converter = new ProjectPackageSetConverter(environment);
            UseExistingPackageSetPreparation preparation = converter.Prepare(request, CancellationToken.None);
            PreparationSummary summary = CreateSummary(preparation);
            WriteSummary(outputPath, summary);
            Debug.Log("Use Existing PackageSet Prepare result: " + outputPath);
        }

        private static PreparationSummary CreateSummary(UseExistingPackageSetPreparation preparation)
        {
            List<EntrySummary> entries = new List<EntrySummary>();
            foreach (PackageSetEntryPlan entry in preparation.Plan.Entries)
            {
                EntrySummary summary = new EntrySummary();
                summary.identity = entry.Placement.Identity;
                summary.originalProjectPath = entry.Placement.OriginalProjectPath;
                summary.projectionPath = entry.Placement.ProjectionPath;
                summary.sourceRepository = entry.SourceRepository;
                summary.observedRepository = entry.ObservedRepository;
                summary.sourceHead = entry.Observation.SourceHead;
                summary.exactPin = entry.Observation.ExactPin;
                summary.projectionState = entry.Observation.ProjectionState.ToString();
                summary.decision = entry.Assessment.Decision.ToString();
                summary.needsPublish = entry.Assessment.NeedsPublish;
                entries.Add(summary);
            }
            entries.Sort(CompareEntries);

            PreparationSummary result = new PreparationSummary();
            result.planPath = preparation.ApplyInstructionsPath;
            result.operationPath = OperationPath(preparation.ApplyInstructionsPath);
            result.exactRevision = preparation.Plan.Resolution.ExactRevision;
            result.alreadyConfigured = preparation.Plan.AlreadyConfigured;
            result.entries = entries.ToArray();
            return result;
        }

        private static int CompareEntries(EntrySummary first, EntrySummary second)
        {
            return StringComparer.Ordinal.Compare(first.identity, second.identity);
        }

        private static string OperationPath(string planPath)
        {
            if (string.IsNullOrWhiteSpace(planPath)) return string.Empty;
            string? operationPath = Path.GetDirectoryName(planPath);
            bool operationMissing = string.IsNullOrWhiteSpace(operationPath);
            if (operationMissing) throw new InvalidOperationException("Prepared plan has no operation directory.");
            string confirmedOperationPath = operationPath!;
            return IO.Full(confirmedOperationPath);
        }

        private static string ReadRequiredAbsolutePath(string argumentName)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string value = string.Empty;
            int matches = 0;
            for (int index = 0; index < arguments.Length; index++)
            {
                if (!arguments[index].Equals(argumentName, StringComparison.Ordinal)) continue;
                matches++;
                if (index + 1 >= arguments.Length) throw new ArgumentException("Missing value for " + argumentName + ".");
                value = arguments[index + 1];
            }
            if (matches != 1 || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Specify exactly one " + argumentName + " argument.");
            bool absolutePath = Path.IsPathRooted(value);
            if (!absolutePath) throw new ArgumentException(argumentName + " must be an absolute path.");
            return IO.Full(value);
        }

        private static void WriteSummary(string outputPath, PreparationSummary summary)
        {
            string? outputDirectory = Path.GetDirectoryName(outputPath);
            bool outputDirectoryMissing = string.IsNullOrWhiteSpace(outputDirectory);
            if (outputDirectoryMissing) throw new InvalidOperationException("Prepare output has no parent directory.");
            string confirmedOutputDirectory = outputDirectory!;
            EnsureSafeOutputDirectory(confirmedOutputDirectory);
            bool outputOccupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(outputPath);
            if (outputOccupied) throw new InvalidOperationException("Prepare output path must be unoccupied: " + outputPath);
            string json = JsonUtility.ToJson(summary, true) + "\n";
            UTF8Encoding encoding = new UTF8Encoding(false);
            byte[] bytes = encoding.GetBytes(json);
            using (FileStream stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void RequirePrivateOutputPath(string projectRoot, string packageSetRoot, string outputPath)
        {
            string projectGitRoot;
            string projectGitDirectory;
            WindowsPackageSetEnvironmentSupport.ResolveProjectGitLocations(projectRoot, out projectGitRoot, out projectGitDirectory);
            string packageSetGitRoot;
            string packageSetGitDirectory;
            WindowsPackageSetEnvironmentSupport.ResolveProjectGitLocations(packageSetRoot, out packageSetGitRoot, out packageSetGitDirectory);
            bool insideProject = IsWithinOrSame(projectGitRoot, outputPath);
            bool insideProjectGitDirectory = IsWithinOrSame(projectGitDirectory, outputPath);
            bool insidePackageSet = IsWithinOrSame(packageSetGitRoot, outputPath);
            bool insidePackageSetGitDirectory = IsWithinOrSame(packageSetGitDirectory, outputPath);
            if (insideProject || insideProjectGitDirectory || insidePackageSet || insidePackageSetGitDirectory)
                throw new InvalidOperationException("Prepare output must be outside the Project and authoritative PackageSet Git trees.");
            bool outputOccupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(outputPath);
            if (outputOccupied) throw new InvalidOperationException("Prepare output path must be unoccupied: " + outputPath);
            string? outputDirectory = Path.GetDirectoryName(outputPath);
            bool outputDirectoryMissing = string.IsNullOrWhiteSpace(outputDirectory);
            if (outputDirectoryMissing) throw new InvalidOperationException("Prepare output has no parent directory.");
            string confirmedOutputDirectory = outputDirectory!;
            RequireSafeExistingOutputAncestors(confirmedOutputDirectory);
        }

        private static bool IsWithinOrSame(string root, string candidate)
        {
            string fullRoot = IO.Full(root);
            string fullCandidate = IO.Full(candidate);
            bool same = fullCandidate.Equals(fullRoot, StringComparison.OrdinalIgnoreCase);
            return same || IO.Within(fullRoot, fullCandidate);
        }

        private static void RequireSafeExistingOutputAncestors(string directory)
        {
            List<string> ancestors = OutputAncestors(directory);
            foreach (string ancestor in ancestors)
            {
                bool occupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(ancestor);
                if (!occupied) continue;
                RequireOrdinaryDirectory(ancestor);
            }
        }

        private static void EnsureSafeOutputDirectory(string directory)
        {
            List<string> ancestors = OutputAncestors(directory);
            foreach (string ancestor in ancestors)
            {
                bool occupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(ancestor);
                if (!occupied) Directory.CreateDirectory(ancestor);
                RequireOrdinaryDirectory(ancestor);
            }
            IO.NoReparseAncestors(directory);
        }

        private static List<string> OutputAncestors(string directory)
        {
            List<string> ancestors = new List<string>();
            string fullDirectory = IO.Full(directory);
            DirectoryInfo? current = new DirectoryInfo(fullDirectory);
            while (current != null)
            {
                ancestors.Add(current.FullName);
                current = current.Parent;
            }
            ancestors.Reverse();
            return ancestors;
        }

        private static void RequireOrdinaryDirectory(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            if (!directory || reparse)
                throw new InvalidOperationException("Prepare output ancestor must be an ordinary directory: " + path);
        }
    }
}
