using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using IO = CraftyRacoon.PackageManagement.Unity3D.Editor.Development.DevelopmentBootstrapIO;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    internal static class PackageSetProjectConfiguration
    {
        internal const string SharedRelativePath = "PackageManagement/project.packageset.json";

        internal static ProjectPackageSetDocument LoadProject(string projectRoot)
        {
            string discoveredGitRoot = IO.Git(projectRoot, "rev-parse", "--show-toplevel");
            string gitRoot = IO.Full(discoveredGitRoot);
            string platformRelativePath = SharedRelativePath.Replace('/', Path.DirectorySeparatorChar);
            string path = Path.Combine(gitRoot, platformRelativePath);
            string json;
            bool found = TryReadDurableDocument(path, "Project PackageSet declaration", out json);
            if (!found)
            {
                ProjectPackageSetDocument empty = new ProjectPackageSetDocument();
                empty.schemaVersion = 3;
                empty.managedProjectionRoot = "Assets/CraftyRacoon/Managed";
                return empty;
            }

            List<string> diagnostics = new List<string>();
            bool validShape = StrictBindingJsonShape.ValidateProject(json, diagnostics);
            if (!validShape)
            {
                string[] diagnosticItems = diagnostics.ToArray();
                string diagnosticText = string.Join("; ", diagnosticItems);
                throw new InvalidOperationException("Project PackageSet declaration has an invalid JSON shape: " + diagnosticText);
            }
            ProjectPackageSetDocument? document = JsonUtility.FromJson<ProjectPackageSetDocument>(json);
            if (document == null || document.schemaVersion != 3) throw new InvalidOperationException("Project PackageSet declaration must use schemaVersion 3.");
            return document;
        }

        internal static LocalPackageSetStateDocument LoadLocal(string projectRoot)
        {
            string path = LocalPath(projectRoot);
            string json;
            bool found = TryReadDurableDocument(path, "Local PackageSet state", out json);
            if (!found) return new LocalPackageSetStateDocument();

            List<string> diagnostics = new List<string>();
            bool validShape = StrictBindingJsonShape.ValidateLocalCheckout(json, diagnostics);
            if (!validShape)
            {
                string[] diagnosticItems = diagnostics.ToArray();
                string diagnosticText = string.Join("; ", diagnosticItems);
                throw new InvalidOperationException("Local PackageSet state has an invalid JSON shape: " + diagnosticText);
            }
            LocalPackageSetStateDocument? document = JsonUtility.FromJson<LocalPackageSetStateDocument>(json);
            if (document == null || document.schemaVersion != 1) throw new InvalidOperationException("Unsupported local PackageSet state.");
            return document;
        }

        private static bool TryReadDurableDocument(string path, string description, out string json)
        {
            json = string.Empty;
            bool occupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(path);
            if (!occupied) return false;
            bool regularFile = WindowsPackageSetEnvironmentSupport.IsRegularDurableFile(path);
            string invalidFileMessage = description + " must be an ordinary durable file: " + path;
            if (!regularFile) throw new InvalidOperationException(invalidFileMessage);
            WindowsPackageSetEnvironmentSupport.DurableFileSnapshot snapshot = WindowsPackageSetEnvironmentSupport.OpenDurableFileSnapshot(path);
            using (snapshot)
            {
                json = snapshot.Text;
            }
            return true;
        }

        private static string LocalPath(string projectRoot)
        {
            string discoveredGitRoot = IO.Git(projectRoot, "rev-parse", "--show-toplevel");
            string gitRoot = IO.Full(discoveredGitRoot);
            string discoveredGitDirectory = IO.Git(gitRoot, "rev-parse", "--absolute-git-dir");
            string gitDirectory = IO.Full(discoveredGitDirectory);
            string path = Path.Combine(gitDirectory, "PackageManagement", "local-checkout.json");
            return path;
        }
    }
}
