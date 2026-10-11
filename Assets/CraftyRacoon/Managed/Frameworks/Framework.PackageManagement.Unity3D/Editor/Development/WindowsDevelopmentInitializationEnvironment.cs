using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CraftyRacoon.PackageManagement.Development;
using CraftyRacoon.PackageManagement.Framework.Development;
using UnityEngine;
using IO = CraftyRacoon.PackageManagement.Unity3D.Editor.Development.DevelopmentBootstrapIO;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    internal static class WindowsPackageSetEnvironmentSupport
    {
        internal sealed class DurableFileSnapshot : IDisposable
        {
            private readonly FileStream stream;

            internal DurableFileSnapshot(FileStream stream, byte[] bytes, string text, string fingerprint)
            {
                this.stream = stream;
                Bytes = bytes;
                Text = text;
                Fingerprint = fingerprint;
            }

            internal byte[] Bytes { get; }
            internal string Text { get; }
            internal string Fingerprint { get; }

            public void Dispose()
            {
                stream.Dispose();
            }
        }

        internal sealed class GitmodulesSection
        {
            internal GitmodulesSection(string name, string sectionKey, string path, string repository)
            {
                Name = name;
                SectionKey = sectionKey;
                Path = path;
                Repository = repository;
            }

            internal string Name { get; }
            internal string SectionKey { get; }
            internal string Path { get; }
            internal string Repository { get; }
        }

        private static readonly string[] RuntimeIdentities =
        {
            "Framework.PackageManagement",
            "Framework.PackageManagement.Unity3D",
            "Module.PackageManagement"
        };

        internal static ProjectWorkflowObservation ObserveProject(string projectRoot, out string gitRoot, out string gitDirectory)
        {
            ResolveProjectGitLocations(projectRoot, out gitRoot, out gitDirectory);
            string projectHead = IO.Git(gitRoot, "rev-parse", "--verify", "HEAD");
            string indexPath = Path.Combine(gitDirectory, "index");
            string gitmodulesPath = Path.Combine(gitRoot, ".gitmodules");
            string indexFingerprint = IO.HashFile(indexPath);
            string gitmodulesFingerprint = IO.HashFile(gitmodulesPath);
            string evidenceToken = CreateProjectEvidence(projectRoot, gitRoot, gitDirectory, projectHead, indexFingerprint, gitmodulesFingerprint);
            string verifiedEvidenceToken = CreateProjectEvidence(projectRoot, gitRoot, gitDirectory, projectHead, indexFingerprint, gitmodulesFingerprint);
            string verifiedProjectHead = IO.Git(gitRoot, "rev-parse", "--verify", "HEAD");
            string verifiedIndexFingerprint = IO.HashFile(indexPath);
            string verifiedGitmodulesFingerprint = IO.HashFile(gitmodulesPath);
            bool headChanged = !verifiedProjectHead.Equals(projectHead, StringComparison.OrdinalIgnoreCase);
            bool indexChanged = !verifiedIndexFingerprint.Equals(indexFingerprint, StringComparison.Ordinal);
            bool gitmodulesChanged = !verifiedGitmodulesFingerprint.Equals(gitmodulesFingerprint, StringComparison.Ordinal);
            bool evidenceChanged = !verifiedEvidenceToken.Equals(evidenceToken, StringComparison.Ordinal);
            if (headChanged || indexChanged || gitmodulesChanged || evidenceChanged)
                throw new InvalidOperationException("Project Git state changed while it was being observed.");
            return new ProjectWorkflowObservation(projectHead, indexFingerprint, evidenceToken);
        }

        internal static void ResolveProjectGitLocations(string projectRoot, out string gitRoot, out string gitDirectory)
        {
            string discoveredGitRoot = IO.Git(projectRoot, "rev-parse", "--show-toplevel");
            gitRoot = IO.Full(discoveredGitRoot);
            string discoveredGitDirectory = IO.Git(gitRoot, "rev-parse", "--absolute-git-dir");
            gitDirectory = IO.Full(discoveredGitDirectory);
        }

        private static string CreateProjectEvidence(string projectRoot, string gitRoot, string gitDirectory, string projectHead, string indexFingerprint, string gitmodulesFingerprint)
        {
            StringBuilder evidence = new StringBuilder();
            evidence.Append(projectHead);
            evidence.Append('\n');
            evidence.Append(indexFingerprint);
            evidence.Append('\n');
            evidence.Append(gitmodulesFingerprint);
            evidence.Append('\n');
            string statusEvidence = IO.Git(gitRoot, "status", "--porcelain=v2", "-z", "--untracked-files=all");
            string diffEvidence = IO.Git(gitRoot, "diff", "--no-ext-diff", "--no-textconv", "--binary", "HEAD", "--", ".");
            evidence.Append(statusEvidence);
            evidence.Append('\n');
            evidence.Append(diffEvidence);
            evidence.Append('\n');
            AppendFileEvidence(evidence, gitRoot, "PackageManagement/project.packageset.json");
            AppendFileEvidence(evidence, gitRoot, "PackageManagement/runtime.bindings.json");
            AppendOperationJournalEvidence(evidence, gitDirectory);
            string assetsRoot = Path.Combine(projectRoot, "Assets");
            AppendAssetsEvidence(evidence, assetsRoot, assetsRoot);
            using (SHA256 hash = SHA256.Create())
            {
                string evidenceText = evidence.ToString();
                byte[] bytes = Encoding.UTF8.GetBytes(evidenceText);
                byte[] fingerprintBytes = hash.ComputeHash(bytes);
                return IO.Hex(fingerprintBytes);
            }
        }

        private static void AppendFileEvidence(StringBuilder evidence, string root, string relative)
        {
            string platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
            string path = Path.Combine(root, platformRelative);
            string fingerprint = DurablePathFingerprint(path);
            evidence.Append(relative);
            evidence.Append('=');
            evidence.Append(fingerprint);
            evidence.Append('\n');
        }

        private static void AppendOperationJournalEvidence(StringBuilder evidence, string gitDirectory)
        {
            string managementRoot = Path.Combine(gitDirectory, "PackageManagement");
            FileAttributes managementAttributes;
            if (TryGetPathAttributes(managementRoot, out managementAttributes))
            {
                bool managementDirectory = (managementAttributes & FileAttributes.Directory) != 0;
                bool managementReparse = (managementAttributes & FileAttributes.ReparsePoint) != 0;
                if (!managementDirectory || managementReparse)
                {
                    int attributeValue = (int)managementAttributes;
                    evidence.Append("operation-parent=<non-regular:");
                    evidence.Append(attributeValue);
                    evidence.Append(">\n");
                    return;
                }
            }
            string operationsRoot = Path.Combine(managementRoot, "operations");
            FileAttributes operationsAttributes;
            if (!TryGetPathAttributes(operationsRoot, out operationsAttributes)) return;
            bool directory = (operationsAttributes & FileAttributes.Directory) != 0;
            bool reparse = (operationsAttributes & FileAttributes.ReparsePoint) != 0;
            if (!directory || reparse)
            {
                int attributeValue = (int)operationsAttributes;
                evidence.Append("operations=<non-regular:");
                evidence.Append(attributeValue);
                evidence.Append(">\n");
                return;
            }
            IEnumerable<string> fileSystemEntries = Directory.EnumerateFileSystemEntries(operationsRoot);
            List<string> entries = new List<string>(fileSystemEntries);
            entries.Sort(StringComparer.Ordinal);
            foreach (string entry in entries)
            {
                FileAttributes entryAttributes = File.GetAttributes(entry);
                bool entryDirectory = (entryAttributes & FileAttributes.Directory) != 0;
                bool entryReparse = (entryAttributes & FileAttributes.ReparsePoint) != 0;
                string name = Path.GetFileName(entry);
                if (!entryDirectory || entryReparse)
                {
                    int attributeValue = (int)entryAttributes;
                    evidence.Append("operation-entry:");
                    evidence.Append(name);
                    evidence.Append('=');
                    evidence.Append(attributeValue);
                    evidence.Append('\n');
                    continue;
                }
                string journalPath = Path.Combine(entry, "journal.json");
                if (DurablePathOccupied(journalPath))
                {
                    string journalFingerprint = DurablePathFingerprint(journalPath);
                    evidence.Append("operation-journal:");
                    evidence.Append(name);
                    evidence.Append('=');
                    evidence.Append(journalFingerprint);
                    evidence.Append('\n');
                }
                string leasePath = Path.Combine(entry, "apply.lock");
                if (DurablePathOccupied(leasePath))
                {
                    string leaseFingerprint = DurablePathFingerprint(leasePath);
                    evidence.Append("operation-lease:");
                    evidence.Append(name);
                    evidence.Append('=');
                    evidence.Append(leaseFingerprint);
                    evidence.Append('\n');
                }
            }
        }

        internal static bool DurablePathOccupied(string path)
        {
            FileAttributes attributes;
            return TryGetPathAttributes(path, out attributes);
        }

        internal static bool IsRegularDurableFile(string path)
        {
            FileAttributes attributes;
            if (!TryGetPathAttributes(path, out attributes)) return false;
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            return !directory && !reparse;
        }

        internal static string DurablePathFingerprint(string path)
        {
            FileAttributes attributes;
            if (!TryGetPathAttributes(path, out attributes)) return "missing";
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            if (!directory && !reparse) return IO.HashFile(path);
            int attributeValue = (int)attributes;
            string attributeText = attributeValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string marker = "non-regular:" + attributeText;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] markerBytes = Encoding.UTF8.GetBytes(marker);
                byte[] fingerprintBytes = hash.ComputeHash(markerBytes);
                return IO.Hex(fingerprintBytes);
            }
        }

        internal static DurableFileSnapshot OpenDurableFileSnapshot(string path)
        {
            IO.NoReparseAncestors(path);
            bool regularFile = IsRegularDurableFile(path);
            if (!regularFile) throw new InvalidOperationException("Durable binding must be an ordinary file: " + path);
            FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                bool stillRegularFile = IsRegularDurableFile(path);
                if (!stillRegularFile) throw new InvalidOperationException("Durable binding changed while it was being opened: " + path);
                if (stream.Length > 1024 * 1024) throw new InvalidOperationException("Durable binding exceeds the one MiB safety limit: " + path);
                int length = checked((int)stream.Length);
                byte[] bytes = new byte[length];
                int total = 0;
                while (total < bytes.Length)
                {
                    int read = stream.Read(bytes, total, bytes.Length - total);
                    if (read == 0) throw new EndOfStreamException("Durable binding ended before its declared length: " + path);
                    total += read;
                }
                UTF8Encoding strictUtf8 = new UTF8Encoding(false, true);
                string text = strictUtf8.GetString(bytes);
                string fingerprint;
                using (SHA256 hash = SHA256.Create())
                {
                    byte[] fingerprintBytes = hash.ComputeHash(bytes);
                    fingerprint = IO.Hex(fingerprintBytes);
                }
                return new DurableFileSnapshot(stream, bytes, text, fingerprint);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static bool TryGetPathAttributes(string path, out FileAttributes attributes)
        {
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                attributes = 0;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                attributes = 0;
                return false;
            }
        }

        private static void AppendAssetsEvidence(StringBuilder evidence, string assetsRoot, string directory)
        {
            if (!Directory.Exists(directory)) return;
            IEnumerable<string> fileSystemEntries = Directory.EnumerateFileSystemEntries(directory);
            List<string> entries = new List<string>(fileSystemEntries);
            entries.Sort(StringComparer.Ordinal);
            foreach (string entry in entries)
            {
                string relative = IO.Relative(assetsRoot, entry);
                if (IO.Reparse(entry))
                {
                    string target;
                    try
                    {
                        target = IO.JunctionTarget(entry);
                    }
                    catch (Exception)
                    {
                        target = "<invalid-junction>";
                    }
                    evidence.Append("junction:");
                    evidence.Append(relative);
                    evidence.Append('=');
                    evidence.Append(target);
                    evidence.Append('\n');
                    continue;
                }
                if (Directory.Exists(entry))
                {
                    AppendAssetsEvidence(evidence, assetsRoot, entry);
                    continue;
                }
                if (entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    string fingerprint = IO.HashFile(entry);
                    evidence.Append("meta:");
                    evidence.Append(relative);
                    evidence.Append('=');
                    evidence.Append(fingerprint);
                    evidence.Append('\n');
                }
            }
        }

        internal static IReadOnlyList<string> ValidateCommon(string projectRoot, string packageSetRoot, string helperPath, bool requireEmptyPackageSet)
        {
            List<string> conflicts = new List<string>();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                conflicts.Add("Development backing currently requires Windows NTFS Junctions.");

            string[] unityChildren = { "Assets", "Packages", "ProjectSettings" };
            foreach (string child in unityChildren)
            {
                string childPath = Path.Combine(projectRoot, child);
                bool childExists = Directory.Exists(childPath);
                if (!childExists) conflicts.Add("Invalid Unity root: " + child + " is missing.");
            }

            bool helperExists = File.Exists(helperPath);
            if (!helperExists) conflicts.Add("Offline apply helper is missing: " + helperPath);

            string gitRoot = string.Empty;
            string gitDirectory = string.Empty;
            try
            {
                string discoveredGitRoot = IO.Git(projectRoot, "rev-parse", "--show-toplevel");
                gitRoot = IO.Full(discoveredGitRoot);
                string discoveredGitDirectory = IO.Git(gitRoot, "rev-parse", "--absolute-git-dir");
                gitDirectory = IO.Full(discoveredGitDirectory);
                IO.Git(gitRoot, "rev-parse", "--verify", "HEAD");
                string projectStatus = IO.Git(gitRoot, "status", "--porcelain=v2", "--untracked-files=all");
                if (projectStatus.Length != 0) conflicts.Add("Commit or remove all tracked, staged, and untracked Project changes before initialization.");
                string indexLockPath = Path.Combine(gitDirectory, "index.lock");
                if (IO.Exists(indexLockPath))
                    conflicts.Add("Another Git operation owns index.lock.");
                AuditOperationJournals(gitDirectory, conflicts);
            }
            catch (Exception error)
            {
                conflicts.Add("Project Git baseline is unavailable: " + error.Message);
            }

            string[] roots = { projectRoot, packageSetRoot };
            foreach (string root in roots)
            {
                try
                {
                    string fullRoot = IO.Full(root);
                    string volume = Path.GetPathRoot(fullRoot)!;
                    bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
                    if (volume.StartsWith(@"\\", StringComparison.Ordinal))
                        conflicts.Add("Initialization supports local NTFS volumes only: " + fullRoot);
                    else if (windows)
                    {
                        DriveInfo drive = new DriveInfo(volume);
                        if (drive.DriveFormat != "NTFS") conflicts.Add("Initialization supports local NTFS volumes only: " + fullRoot);
                    }
                    IO.NoReparseAncestors(fullRoot);
                }
                catch (Exception error)
                {
                    conflicts.Add(root + ": " + error.Message);
                }
            }

            if (!string.IsNullOrWhiteSpace(gitRoot))
            {
                string packageRoot = IO.Full(packageSetRoot);
                bool sameRoot = packageRoot.Equals(gitRoot, StringComparison.OrdinalIgnoreCase);
                bool packageWithinProject = IO.Within(gitRoot, packageRoot);
                bool projectWithinPackage = IO.Within(packageRoot, gitRoot);
                bool nested = packageWithinProject || projectWithinPackage;
                if (sameRoot || nested) conflicts.Add("PackageSet must be external to the Project Git tree, not its ancestor or descendant.");
            }

            if (requireEmptyPackageSet && IO.Exists(packageSetRoot))
            {
                IEnumerator<string>? enumerator = null;
                bool emptyDirectory = false;
                if (Directory.Exists(packageSetRoot))
                {
                    IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(packageSetRoot);
                    enumerator = entries.GetEnumerator();
                    emptyDirectory = !enumerator.MoveNext();
                    enumerator.Dispose();
                }
                if (!emptyDirectory) conflicts.Add("Create PackageSet requires an absent or empty destination.");
            }

            return conflicts;
        }

        internal static void AuditOperationJournals(string gitDirectory, ICollection<string> conflicts)
        {
            string managementRoot = Path.Combine(gitDirectory, "PackageManagement");
            string operationsRoot = Path.Combine(managementRoot, "operations");
            try
            {
                if (DurablePathOccupied(managementRoot))
                {
                    FileAttributes managementAttributes = File.GetAttributes(managementRoot);
                    bool managementDirectory = (managementAttributes & FileAttributes.Directory) != 0;
                    bool managementReparse = (managementAttributes & FileAttributes.ReparsePoint) != 0;
                    if (!managementDirectory || managementReparse)
                    {
                        conflicts.Add("Package Management private evidence root is not an ordinary directory: " + managementRoot);
                        return;
                    }
                }
                IO.NoReparseAncestors(managementRoot);
                if (!DurablePathOccupied(operationsRoot))
                {
                    IO.NoReparseAncestors(operationsRoot);
                    return;
                }
                FileAttributes operationsAttributes = File.GetAttributes(operationsRoot);
                bool operationsDirectory = (operationsAttributes & FileAttributes.Directory) != 0;
                bool operationsReparse = (operationsAttributes & FileAttributes.ReparsePoint) != 0;
                if (!operationsDirectory || operationsReparse)
                {
                    conflicts.Add("Package Management operations root is not an ordinary directory: " + operationsRoot);
                    return;
                }
                IO.NoReparseAncestors(operationsRoot);
                foreach (string operationPath in Directory.EnumerateFileSystemEntries(operationsRoot))
                {
                    FileAttributes operationAttributes = File.GetAttributes(operationPath);
                    bool directory = (operationAttributes & FileAttributes.Directory) != 0;
                    bool reparse = (operationAttributes & FileAttributes.ReparsePoint) != 0;
                    if (!directory || reparse)
                    {
                        conflicts.Add("Unknown Package Management operation evidence must be preserved and inspected: " + operationPath);
                        continue;
                    }
                    string leasePath = Path.Combine(operationPath, "apply.lock");
                    if (DurablePathOccupied(leasePath))
                    {
                        conflicts.Add("Package Management operation has an active or stale Apply lease that must be preserved and inspected: " + leasePath);
                    }
                    string journalPath = Path.Combine(operationPath, "journal.json");
                    if (!DurablePathOccupied(journalPath)) continue;
                    if (!IsRegularDurableFile(journalPath))
                    {
                        conflicts.Add("Package Management recovery journal is not an ordinary file: " + journalPath);
                        continue;
                    }
                    AuditOperationJournal(journalPath, conflicts);
                }
            }
            catch (Exception error)
            {
                conflicts.Add("Package Management operation evidence could not be audited at " + operationsRoot + ": " + error.Message);
            }
        }

        private static void AuditOperationJournal(string journalPath, ICollection<string> conflicts)
        {
            try
            {
                using (DurableFileSnapshot snapshot = OpenDurableFileSnapshot(journalPath))
                {
                    StrictJsonValue journal = StrictJsonDocument.Parse(snapshot.Text);
                    if (!IsToolJournalShape(journal))
                    {
                        conflicts.Add("Package Management recovery journal has an unknown format: " + journalPath);
                        return;
                    }
                    string status = journal.Members["status"].Text;
                    bool validated = status.Equals("validated", StringComparison.Ordinal);
                    bool applied = status.Equals("applied", StringComparison.Ordinal);
                    bool rolledBack = status.Equals("rolled-back", StringComparison.Ordinal);
                    bool preflightFailed = status.Equals("preflight-failed", StringComparison.Ordinal);
                    bool safe = validated || applied || rolledBack || preflightFailed;
                    if (safe) return;
                    bool applying = status.Equals("applying", StringComparison.Ordinal);
                    bool needsRecovery = status.Equals("needs-recovery", StringComparison.Ordinal);
                    bool recovery = applying || needsRecovery;
                    string description = recovery ? status : "unknown status '" + status + "'";
                    conflicts.Add("Package Management operation requires recovery inspection at " + journalPath + " (" + description + "). Preserve it and do not start another workflow.");
                }
            }
            catch (Exception error)
            {
                conflicts.Add("Package Management recovery journal is malformed at " + journalPath + ": " + error.Message);
            }
        }

        private static bool IsToolJournalShape(StrictJsonValue journal)
        {
            if (journal.Kind != StrictJsonValueKind.Object) return false;
            bool legacyShape = journal.Members.Count == 5;
            bool directoriesValid = JournalMemberHasKind(journal, "directories", StrictJsonValueKind.Array);
            bool currentShape = journal.Members.Count == 6 && directoriesValid;
            if (!legacyShape && !currentShape) return false;
            bool statusValid = JournalMemberHasKind(journal, "status", StrictJsonValueKind.String);
            bool movesValid = JournalMemberHasKind(journal, "moves", StrictJsonValueKind.Array);
            bool linksValid = JournalMemberHasKind(journal, "links", StrictJsonValueKind.Array);
            bool filesValid = JournalMemberHasKind(journal, "files", StrictJsonValueKind.Array);
            bool errorValid = JournalMemberHasKind(journal, "error", StrictJsonValueKind.String);
            return statusValid && movesValid && linksValid && filesValid && errorValid;
        }

        private static bool JournalMemberHasKind(StrictJsonValue journal, string name, StrictJsonValueKind kind)
        {
            StrictJsonValue member;
            return journal.Members.TryGetValue(name, out member) && member.Kind == kind;
        }

        internal static bool IsRuntimeIdentity(string identity)
        {
            foreach (string runtimeIdentity in RuntimeIdentities)
            {
                if (identity.Equals(runtimeIdentity, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal static bool IsIndependentGitCheckout(string path)
        {
            try
            {
                string discoveredRoot = IO.Git(path, "rev-parse", "--show-toplevel");
                string root = IO.Full(discoveredRoot);
                string fullPath = IO.Full(path);
                return root.Equals(fullPath, StringComparison.OrdinalIgnoreCase);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool ContainsGitOwnership(string root)
        {
            if (!Directory.Exists(root)) return false;
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    string name = Path.GetFileName(entry);
                    if (name.Equals(".git", StringComparison.Ordinal)) return true;
                    FileAttributes attributes = File.GetAttributes(entry);
                    bool childDirectory = (attributes & FileAttributes.Directory) != 0;
                    bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
                    if (childDirectory && !reparse) pending.Push(entry);
                }
            }
            return false;
        }

        internal static DevelopmentSourceKind ParseKind(string value)
        {
            if (value.Equals("GitSubmodule", StringComparison.Ordinal)) return DevelopmentSourceKind.GitSubmodule;
            if (value.Equals("LocalDraft", StringComparison.Ordinal)) return DevelopmentSourceKind.LocalDraft;
            throw new InvalidOperationException("Unknown source kind: " + value);
        }

        internal static string RequireAssetsProjectionRoot(string value)
        {
            string normalized = PackageRelativePath.Normalize(value);
            bool insideAssets = normalized.StartsWith("Assets/", StringComparison.Ordinal);
            if (!insideAssets) throw new InvalidOperationException("Managed projection root must be an ordinary child of Assets.");
            return normalized;
        }

        internal static string RequireOrdinaryAssetsProjectionRoot(string projectRoot, string value)
        {
            string normalized = RequireAssetsProjectionRoot(value);
            string assetsPath = Path.Combine(projectRoot, "Assets");
            string assets = IO.Full(assetsPath);
            string absolute = IO.Under(projectRoot, normalized);
            if (!IO.Within(assets, absolute)) throw new InvalidOperationException("Managed projection root must be inside Assets.");
            IO.NoReparseAncestors(absolute);
            if (!IO.Exists(absolute)) return normalized;
            FileAttributes attributes = File.GetAttributes(absolute);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            if (!directory || reparse) throw new InvalidOperationException("Managed projection root must be an ordinary directory when it exists.");
            return normalized;
        }

        internal static string Projection(PackageSetEntryDocument entry)
        {
            bool projectionPathMissing = string.IsNullOrWhiteSpace(entry.projectionPath);
            if (projectionPathMissing) throw new InvalidOperationException("PackageSet entry projectionPath is required.");
            return PackageRelativePath.Normalize(entry.projectionPath);
        }

        internal static PackageSetDocument ReadPackageSetDocument(string packageSetRoot)
        {
            string manifestPath = Path.Combine(packageSetRoot, "package-set.json");
            bool manifestExists = File.Exists(manifestPath);
            if (!manifestExists) throw new InvalidOperationException("PackageSet checkout does not contain package-set.json.");
            string json = File.ReadAllText(manifestPath);
            List<string> diagnostics = new List<string>();
            bool validShape = StrictBindingJsonShape.ValidatePackageSet(json, diagnostics);
            if (!validShape)
            {
                string[] diagnosticItems = diagnostics.ToArray();
                string diagnosticText = string.Join("; ", diagnosticItems);
                throw new InvalidOperationException("PackageSet manifest has an invalid JSON shape: " + diagnosticText);
            }
            PackageSetDocument? document = JsonUtility.FromJson<PackageSetDocument>(json);
            if (document == null || document.schemaVersion != 2 || document.entries == null)
                throw new InvalidOperationException("Unsupported PackageSet manifest.");
            bool identityMissing = string.IsNullOrWhiteSpace(document.identity);
            if (identityMissing) throw new InvalidOperationException("PackageSet manifest identity is empty.");
            PackageSetTargetDocument? target = document.target;
            bool targetMissing = target == null;
            bool targetIdentityMissing = target != null && string.IsNullOrWhiteSpace(target.identity);
            if (targetMissing || targetIdentityMissing) throw new InvalidOperationException("PackageSet manifest target identity is required.");
            bool sourceRootMissing = string.IsNullOrWhiteSpace(document.sourceRoot);
            if (sourceRootMissing) throw new InvalidOperationException("PackageSet manifest sourceRoot is required.");
            bool projectionRootMissing = string.IsNullOrWhiteSpace(document.managedProjectionRoot);
            if (projectionRootMissing) throw new InvalidOperationException("PackageSet manifest managedProjectionRoot is required.");
            string identity = PackageRelativePath.Normalize(document.identity);
            bool identityIsPath = identity.Contains("/");
            if (identityIsPath) throw new InvalidOperationException("PackageSet manifest identity must be canonical, not a path.");
            PackageRelativePath.Normalize(document.sourceRoot);
            RequireAssetsProjectionRoot(document.managedProjectionRoot);
            bool targetRepositoryMissing = target != null && string.IsNullOrWhiteSpace(target.repository);
            if (targetRepositoryMissing) throw new InvalidOperationException("PackageSet manifest target repository is required.");
            foreach (PackageSetEntryDocument entry in document.entries)
            {
                if (entry == null) throw new InvalidOperationException("PackageSet manifest contains a null entry.");
                bool entryIdentityMissing = string.IsNullOrWhiteSpace(entry.identity);
                bool sourcePathMissing = string.IsNullOrWhiteSpace(entry.sourcePath);
                bool projectionPathMissing = string.IsNullOrWhiteSpace(entry.projectionPath);
                bool kindMissing = string.IsNullOrWhiteSpace(entry.kind);
                if (entryIdentityMissing || sourcePathMissing || projectionPathMissing || kindMissing) throw new InvalidOperationException("PackageSet manifest entries require identity, sourcePath, projectionPath and kind.");
            }
            return document;
        }

        internal static string ReadOrdinaryHeadFileEvidence(string checkoutRoot, string relative)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            string absolute = IO.Under(checkoutRoot, normalized);
            bool regularFile = IsRegularDurableFile(absolute);
            if (!regularFile) throw new InvalidOperationException("PackageSet authority file must be an ordinary worktree file: " + normalized);
            string treeOutput = IO.Git(checkoutRoot, "ls-tree", "-z", "HEAD", "--", normalized);
            string[] records = treeOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            if (records.Length != 1) throw new InvalidOperationException("PackageSet authority file must have exactly one HEAD tree entry: " + normalized);
            string record = records[0];
            int tab = record.IndexOf('\t');
            if (tab <= 0) throw new InvalidOperationException("PackageSet authority file has malformed HEAD tree evidence: " + normalized);
            string header = record.Substring(0, tab);
            string indexedPath = record.Substring(tab + 1);
            string[] headerParts = header.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (headerParts.Length != 3) throw new InvalidOperationException("PackageSet authority file has malformed HEAD tree authority: " + normalized);
            string mode = headerParts[0];
            string objectKind = headerParts[1];
            string objectId = headerParts[2];
            bool ordinaryMode = mode.Equals("100644", StringComparison.Ordinal) || mode.Equals("100755", StringComparison.Ordinal);
            bool ordinaryBlob = ordinaryMode && objectKind.Equals("blob", StringComparison.Ordinal);
            bool pathMatches = indexedPath.Equals(normalized, StringComparison.Ordinal);
            if (!ordinaryBlob || !pathMatches) throw new InvalidOperationException("PackageSet authority file must be an ordinary HEAD blob with exact path and mode: " + normalized);
            string worktreeObjectId = IO.Git(checkoutRoot, "hash-object", "--no-filters", "--", absolute);
            if (!worktreeObjectId.Equals(objectId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PackageSet authority file raw bytes differ from its exact HEAD blob: " + normalized);
            return mode + " " + objectId;
        }

        internal static void RequirePackageSetOwnedCheckout(string packageSetRoot, string source, string identity)
        {
            bool sourceDirectoryExists = Directory.Exists(source);
            bool sourceReparse = IO.Reparse(source);
            if (!sourceDirectoryExists || sourceReparse)
                throw new InvalidOperationException(identity + ": canonical source must be an ordinary directory, not a reparse point.");
            IO.NoReparseAncestors(source);
            if (!IsIndependentGitCheckout(source))
                throw new InvalidOperationException(identity + ": canonical source is not an independent checkout.");
            string discoveredPackageSetGitDirectory = IO.Git(packageSetRoot, "rev-parse", "--absolute-git-dir");
            string packageSetGitDirectory = IO.Full(discoveredPackageSetGitDirectory);
            string discoveredSourceGitDirectory = IO.Git(source, "rev-parse", "--absolute-git-dir");
            string sourceGitDirectory = IO.Full(discoveredSourceGitDirectory);
            IO.NoReparseAncestors(packageSetGitDirectory);
            IO.NoReparseAncestors(sourceGitDirectory);
            if (!IO.Within(packageSetGitDirectory, sourceGitDirectory))
                throw new InvalidOperationException(identity + ": canonical source Git metadata does not belong to this PackageSet checkout.");
        }

        internal static string ProjectRemote(string gitRoot)
        {
            string remotes = IO.Git(gitRoot, "remote");
            string[] remoteNames = remotes.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string remoteName in remoteNames)
            {
                if (!remoteName.Equals("origin", StringComparison.Ordinal)) continue;
                return IO.Git(gitRoot, "remote", "get-url", "origin");
            }
            return string.Empty;
        }

        internal static string CheckoutRemote(string checkoutRoot)
        {
            string remotes = IO.Git(checkoutRoot, "remote");
            string[] remoteNames = remotes.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string remoteName in remoteNames)
            {
                if (!remoteName.Equals("origin", StringComparison.Ordinal)) continue;
                string repository = IO.Git(checkoutRoot, "remote", "get-url", "origin");
                return repository;
            }
            return string.Empty;
        }

        internal static string EscapeIgnore(string value)
        {
            string escapedOpeningBracket = value.Replace("[", "\\[");
            string escapedBrackets = escapedOpeningBracket.Replace("]", "\\]");
            string escapedSpaces = escapedBrackets.Replace(" ", "\\ ");
            string escapedExclamation = escapedSpaces.Replace("!", "\\!");
            return escapedExclamation.Replace("#", "\\#");
        }

        internal static void PreserveMetadata(string original, string source)
        {
            SortedDictionary<string, string> originalFiles = IO.Files(original);
            foreach (KeyValuePair<string, string> item in originalFiles)
            {
                if (!item.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(source, item.Key);
                if (File.Exists(target))
                {
                    string targetHash = IO.HashFile(target);
                    string sourceHash = IO.HashFile(item.Value);
                    if (!targetHash.Equals(sourceHash, StringComparison.Ordinal))
                        throw new InvalidOperationException("Metadata conflict: " + item.Key);
                    continue;
                }
                IO.Git(source, "check-ignore", "--quiet", "--", item.Key);
                string? parent = Path.GetDirectoryName(target);
                bool parentMissing = string.IsNullOrWhiteSpace(parent);
                if (parentMissing) throw new InvalidOperationException("Metadata destination has no parent: " + target);
                Directory.CreateDirectory(parent!);
                File.Copy(item.Value, target);
            }
        }

        internal static string FindSubmoduleSection(string gitRoot, string relative)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            IReadOnlyList<GitmodulesSection> sections = ReadGitmodules(gitRoot);
            foreach (GitmodulesSection section in sections)
            {
                if (section.Path.Equals(normalized, StringComparison.Ordinal)) return section.SectionKey;
            }
            return string.Empty;
        }

        internal static IReadOnlyList<GitmodulesSection> ReadGitmodules(string gitRoot)
        {
            string modulesPath = Path.Combine(gitRoot, ".gitmodules");
            if (!File.Exists(modulesPath)) return new GitmodulesSection[0];
            bool ordinaryFile = IsRegularDurableFile(modulesPath);
            if (!ordinaryFile) throw new InvalidOperationException(".gitmodules must be an ordinary file.");
            string[] lines = File.ReadAllLines(modulesPath);
            List<string> sectionNames = new List<string>();
            Dictionary<string, HashSet<string>> keysBySection = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            string currentSection = string.Empty;
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    currentSection = ParseSubmoduleSectionHeader(line);
                    if (keysBySection.ContainsKey(currentSection))
                        throw new InvalidOperationException(".gitmodules contains a duplicate section: " + currentSection);
                    sectionNames.Add(currentSection);
                    HashSet<string> newSectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    keysBySection.Add(currentSection, newSectionKeys);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(currentSection))
                    throw new InvalidOperationException(".gitmodules contains a key outside a submodule section.");
                int equals = line.IndexOf('=');
                if (equals <= 0) throw new InvalidOperationException(".gitmodules contains an unsupported key record in section: " + currentSection);
                string key = line.Substring(0, equals).Trim();
                if (!IsGitmodulesKey(key)) throw new InvalidOperationException(".gitmodules contains an invalid key in section " + currentSection + ": " + key);
                HashSet<string> sectionKeys = keysBySection[currentSection];
                if (!sectionKeys.Add(key)) throw new InvalidOperationException(".gitmodules contains a duplicate key in section " + currentSection + ": " + key);
            }

            List<GitmodulesSection> sections = new List<GitmodulesSection>();
            HashSet<string> mappedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string sectionName in sectionNames)
            {
                HashSet<string> sectionKeys = keysBySection[sectionName];
                bool pathDeclared = sectionKeys.Contains("path");
                bool repositoryDeclared = sectionKeys.Contains("url");
                if (!pathDeclared || !repositoryDeclared)
                    throw new InvalidOperationException(".gitmodules submodule section must declare exactly one path and one url: " + sectionName);
                string sectionKey = "submodule." + sectionName;
                string rawPath = IO.Git(gitRoot, "config", "--file", modulesPath, "--get", sectionKey + ".path");
                string repository = IO.Git(gitRoot, "config", "--file", modulesPath, "--get", sectionKey + ".url");
                string portablePath = rawPath.Replace('\\', '/');
                string path = PackageRelativePath.Normalize(portablePath);
                if (string.IsNullOrWhiteSpace(repository))
                    throw new InvalidOperationException(".gitmodules submodule url is empty: " + sectionName);
                if (!mappedPaths.Add(path))
                    throw new InvalidOperationException(".gitmodules maps more than one section to the same path: " + path);
                GitmodulesSection section = new GitmodulesSection(sectionName, sectionKey, path, repository);
                sections.Add(section);
            }
            return sections;
        }

        private static string ParseSubmoduleSectionHeader(string line)
        {
            const string prefix = "[submodule \"";
            const string suffix = "\"]";
            bool prefixMatches = line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            bool suffixMatches = line.EndsWith(suffix, StringComparison.Ordinal);
            if (!prefixMatches || !suffixMatches)
                throw new InvalidOperationException(".gitmodules contains an unsupported section header: " + line);
            int nameLength = line.Length - prefix.Length - suffix.Length;
            if (nameLength <= 0) throw new InvalidOperationException(".gitmodules contains an empty submodule section name.");
            string name = line.Substring(prefix.Length, nameLength);
            foreach (char character in name)
            {
                bool invalid = character == '\\' || character == '"' || char.IsControl(character);
                if (invalid) throw new InvalidOperationException(".gitmodules contains an unsupported submodule section name.");
            }
            return name;
        }

        private static bool IsGitmodulesKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            foreach (char character in key)
            {
                bool letter = character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z';
                bool digit = character >= '0' && character <= '9';
                if (!letter && !digit && character != '-') return false;
            }
            return true;
        }

        internal static bool IndexPathAbsent(string gitRoot, string relative)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            string entries = IO.Git(gitRoot, "ls-files", "--stage", "-z", "--", normalized);
            if (entries.Length != 0) return false;
            return IndexFileMatchesWorktree(gitRoot, normalized + ".meta", false);
        }

        internal static bool IndexFileMatchesWorktree(string gitRoot, string relative, bool fileExists)
        {
            bool trackedInIndex;
            bool indexMatchesWorktree;
            ObserveIndexFile(gitRoot, relative, fileExists, out trackedInIndex, out indexMatchesWorktree);
            if (!fileExists) return !trackedInIndex && indexMatchesWorktree;
            return trackedInIndex && indexMatchesWorktree;
        }

        internal static bool IndexFileMatchesWorktree(string gitRoot, string relative, bool fileExists, string requiredMode)
        {
            bool trackedInIndex;
            bool indexMatchesWorktree;
            ObserveIndexFile(gitRoot, relative, fileExists, requiredMode, out trackedInIndex, out indexMatchesWorktree);
            if (!fileExists) return !trackedInIndex && indexMatchesWorktree;
            return trackedInIndex && indexMatchesWorktree;
        }

        internal static void ObserveIndexFile(string gitRoot, string relative, bool fileExists, out bool trackedInIndex, out bool indexMatchesWorktree)
        {
            ObserveIndexFile(gitRoot, relative, fileExists, string.Empty, out trackedInIndex, out indexMatchesWorktree);
        }

        internal static void ObserveIndexFile(string gitRoot, string relative, bool fileExists, string requiredMode, out bool trackedInIndex, out bool indexMatchesWorktree)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            string absolute = IO.Under(gitRoot, normalized);
            ObserveIndexFileAtPath(gitRoot, normalized, absolute, fileExists, requiredMode, string.Empty, string.Empty, out trackedInIndex, out indexMatchesWorktree);
        }

        private static void ObserveIndexFileAtPath(string gitRoot, string normalized, string absolute, bool fileExists, string requiredMode, string allowedJunction, string expectedJunctionTarget, out bool trackedInIndex, out bool indexMatchesWorktree)
        {
            string entries = IO.Git(gitRoot, "ls-files", "--stage", "-z", "--", normalized);
            string[] records = entries.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            trackedInIndex = records.Length != 0;
            if (!fileExists)
            {
                indexMatchesWorktree = records.Length == 0;
                return;
            }
            if (records.Length != 1)
            {
                indexMatchesWorktree = false;
                return;
            }
            string record = records[0];
            int tab = record.IndexOf('\t');
            if (tab <= 0)
            {
                indexMatchesWorktree = false;
                return;
            }
            string header = record.Substring(0, tab);
            string indexedPath = record.Substring(tab + 1);
            bool ordinaryMode;
            if (string.IsNullOrWhiteSpace(requiredMode))
            {
                bool ordinaryFile = header.StartsWith("100644 ", StringComparison.Ordinal);
                bool executableFile = header.StartsWith("100755 ", StringComparison.Ordinal);
                ordinaryMode = ordinaryFile || executableFile;
            }
            else
            {
                string requiredPrefix = requiredMode + " ";
                ordinaryMode = header.StartsWith(requiredPrefix, StringComparison.Ordinal);
            }
            bool stageZero = header.EndsWith(" 0", StringComparison.Ordinal);
            bool pathMatches = indexedPath.Equals(normalized, StringComparison.Ordinal);
            if (!ordinaryMode || !stageZero || !pathMatches)
            {
                indexMatchesWorktree = false;
                return;
            }
            bool pathSafe = IsSafeObservedWorktreePath(gitRoot, normalized, absolute, allowedJunction, expectedJunctionTarget);
            bool worktreeFileExists = File.Exists(absolute);
            bool worktreeFileReparse = IO.Reparse(absolute);
            if (!pathSafe || !worktreeFileExists || worktreeFileReparse)
            {
                indexMatchesWorktree = false;
                return;
            }
            int firstSpace = header.IndexOf(' ');
            int lastSpace = header.LastIndexOf(' ');
            if (firstSpace <= 0 || lastSpace <= firstSpace)
            {
                indexMatchesWorktree = false;
                return;
            }
            string indexObjectId = header.Substring(firstSpace + 1, lastSpace - firstSpace - 1);
            string worktreeObjectId = IO.Git(gitRoot, "hash-object", "--path=" + normalized, "--", absolute);
            indexMatchesWorktree = indexObjectId.Equals(worktreeObjectId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IndexFileMatchesThroughVerifiedJunction(string gitRoot, string relative, string absolute, bool fileExists, string requiredMode, string allowedJunction, string expectedJunctionTarget)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            bool trackedInIndex;
            bool indexMatchesWorktree;
            ObserveIndexFileAtPath(gitRoot, normalized, absolute, fileExists, requiredMode, allowedJunction, expectedJunctionTarget, out trackedInIndex, out indexMatchesWorktree);
            if (!fileExists) return !trackedInIndex && indexMatchesWorktree;
            return trackedInIndex && indexMatchesWorktree;
        }

        private static bool IsSafeObservedWorktreePath(string gitRoot, string normalized, string absolute, string allowedJunction, string expectedJunctionTarget)
        {
            string platformRelative = normalized.Replace('/', Path.DirectorySeparatorChar);
            string combinedPath = Path.Combine(gitRoot, platformRelative);
            string expectedAbsolute = IO.Full(combinedPath);
            string fullAbsolute = IO.Full(absolute);
            if (!expectedAbsolute.Equals(fullAbsolute, StringComparison.OrdinalIgnoreCase)) return false;
            if (!IO.Within(gitRoot, fullAbsolute)) return false;
            bool junctionAllowed = !string.IsNullOrWhiteSpace(allowedJunction) && !string.IsNullOrWhiteSpace(expectedJunctionTarget);
            if (!junctionAllowed)
            {
                try
                {
                    IO.NoReparseAncestors(fullAbsolute, true);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
            string fullJunction = IO.Full(allowedJunction);
            string fullTarget = IO.Full(expectedJunctionTarget);
            if (!IO.Within(fullJunction, fullAbsolute)) return false;
            if (!IO.Reparse(fullJunction)) return false;
            string actualTarget;
            try
            {
                actualTarget = IO.JunctionTarget(fullJunction);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            if (!actualTarget.Equals(fullTarget, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                IO.NoReparseAncestors(fullTarget);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            string? parent = Path.GetDirectoryName(fullAbsolute);
            if (string.IsNullOrWhiteSpace(parent)) return false;
            DirectoryInfo? current = new DirectoryInfo(parent!);
            while (current != null)
            {
                bool reparse = IO.Reparse(current.FullName);
                bool allowed = current.FullName.Equals(fullJunction, StringComparison.OrdinalIgnoreCase);
                if (reparse && !allowed) return false;
                current = current.Parent;
            }
            return true;
        }

        internal static bool IndexGitlinkMatches(string gitRoot, string relative, string exactRevision)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            string entries = IO.Git(gitRoot, "ls-files", "--stage", "-z", "--", normalized);
            string[] records = entries.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            if (records.Length != 1) return false;
            string expectedPrefix = "160000 " + exactRevision + " 0\t";
            if (!records[0].StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            string expectedSuffix = "\t" + normalized;
            return records[0].EndsWith(expectedSuffix, StringComparison.Ordinal);
        }

        internal static bool ProjectPathIgnored(string gitRoot, string relative)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            try
            {
                IO.Git(gitRoot, "check-ignore", "--no-index", "--quiet", "--", normalized);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool IsSafeGeneratedProjectionExclusion(string sourceRelativePath)
        {
            string normalized = PackageRelativePath.Normalize(sourceRelativePath);
            return normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool OrdinaryProjectionIndexMatches(string gitRoot, string source, string projection, string projectionRelative, bool allowMetadataOverlay)
        {
            return OrdinaryProjectionIndexMatches(gitRoot, source, projection, projectionRelative, allowMetadataOverlay, string.Empty, string.Empty);
        }

        internal static bool OrdinaryProjectionIndexMatches(string gitRoot, string source, string projection, string projectionRelative, bool allowMetadataOverlay, string allowedJunction, string expectedJunctionTarget)
        {
            bool sourceExists = Directory.Exists(source);
            bool projectionExists = Directory.Exists(projection);
            if (!sourceExists || !projectionExists) return false;
            bool junctionAllowed = !string.IsNullOrWhiteSpace(allowedJunction) && !string.IsNullOrWhiteSpace(expectedJunctionTarget);
            if (junctionAllowed)
            {
                string fullProjection = IO.Full(projection);
                string fullAllowedJunction = IO.Full(allowedJunction);
                if (!fullProjection.Equals(fullAllowedJunction, StringComparison.OrdinalIgnoreCase)) return false;
                if (!IO.Reparse(fullAllowedJunction)) return false;
                string actualTarget = IO.JunctionTarget(fullAllowedJunction);
                string fullExpectedTarget = IO.Full(expectedJunctionTarget);
                if (!actualTarget.Equals(fullExpectedTarget, StringComparison.OrdinalIgnoreCase)) return false;
                try
                {
                    IO.NoReparseAncestors(fullExpectedTarget);
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
            else if (IO.Reparse(projection))
            {
                return false;
            }
            Dictionary<string, string> sourceTree = IO.GitTree(source);
            SortedDictionary<string, string> sourceFiles = IO.Files(source);
            SortedDictionary<string, string> projectionFiles = IO.Files(projection);
            if (allowMetadataOverlay && !ConversionMetadataMatches(gitRoot, source, projection, projectionRelative, sourceTree, sourceFiles, projectionFiles)) return false;
            Dictionary<string, string> expectedTree = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> sourceEntry in sourceTree)
            {
                string projectPath = PackageRelativePath.Normalize(projectionRelative + "/" + sourceEntry.Key);
                bool isMetadata = sourceEntry.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
                bool pairedGeneratedMetadata = isMetadata && IsPairedGeneratedMetadataExclusion(gitRoot, source, projectionRelative, sourceTree, sourceEntry.Key, allowedJunction, expectedJunctionTarget);
                if (pairedGeneratedMetadata) continue;
                bool ignored = !isMetadata && ProjectPathIgnored(gitRoot, projectPath);
                bool preservedIgnoredFile = false;
                if (ignored && junctionAllowed)
                {
                    string platformRelative = sourceEntry.Key.Replace('/', Path.DirectorySeparatorChar);
                    string projectionFile = Path.Combine(projection, platformRelative);
                    preservedIgnoredFile = IndexEntryMatchesSourceThroughVerifiedJunction(gitRoot, projectPath, sourceEntry.Value, projectionFile, allowedJunction, expectedJunctionTarget);
                }
                else if (ignored)
                {
                    preservedIgnoredFile = IndexEntryMatchesSource(gitRoot, projectPath, sourceEntry.Value);
                }
                if (ignored && !preservedIgnoredFile && !IsSafeGeneratedProjectionExclusion(sourceEntry.Key)) return false;
                if (ignored && !preservedIgnoredFile) continue;
                if (!projectionFiles.ContainsKey(sourceEntry.Key)) return false;
                expectedTree.Add(sourceEntry.Key, sourceEntry.Value);
            }
            if (!allowMetadataOverlay)
            {
                foreach (KeyValuePair<string, string> sourceFile in sourceFiles)
                {
                    if (!sourceFile.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    if (expectedTree.ContainsKey(sourceFile.Key)) continue;
                    if (IsPairedGeneratedMetadataExclusion(gitRoot, source, projectionRelative, sourceTree, sourceFile.Key, allowedJunction, expectedJunctionTarget)) continue;
                    expectedTree.Add(sourceFile.Key, string.Empty);
                }
            }
            foreach (KeyValuePair<string, string> projectionFile in projectionFiles)
            {
                if (projectionFile.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (expectedTree.ContainsKey(projectionFile.Key)) continue;
                string projectPath = PackageRelativePath.Normalize(projectionRelative + "/" + projectionFile.Key);
                bool safeExclusion = IsSafeGeneratedProjectionExclusion(projectionFile.Key);
                bool authoritativeFile = safeExclusion && sourceTree.ContainsKey(projectionFile.Key);
                bool projectPathIgnored = false;
                if (authoritativeFile) projectPathIgnored = ProjectPathIgnored(gitRoot, projectPath);
                bool ignoredAuthoritativeFile = authoritativeFile && projectPathIgnored;
                if (!ignoredAuthoritativeFile) return false;
            }
            string indexedEntries = IO.Git(gitRoot, "ls-files", "--stage", "-z", "--", projectionRelative);
            string[] indexedRecords = indexedEntries.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, string> indexedTree = new Dictionary<string, string>(StringComparer.Ordinal);
            string prefix = projectionRelative + "/";
            foreach (string indexedRecord in indexedRecords)
            {
                int tab = indexedRecord.IndexOf('\t');
                if (tab <= 0) return false;
                string header = indexedRecord.Substring(0, tab);
                string indexedPath = indexedRecord.Substring(tab + 1);
                bool ordinaryFileMode = header.StartsWith("100644 ", StringComparison.Ordinal);
                bool executableFileMode = header.StartsWith("100755 ", StringComparison.Ordinal);
                bool ordinaryMode = ordinaryFileMode || executableFileMode;
                if (!ordinaryMode || !header.EndsWith(" 0", StringComparison.Ordinal)) return false;
                if (!indexedPath.StartsWith(prefix, StringComparison.Ordinal)) return false;
                string relative = indexedPath.Substring(prefix.Length);
                int firstSpace = header.IndexOf(' ');
                int lastSpace = header.LastIndexOf(' ');
                if (firstSpace <= 0 || lastSpace <= firstSpace) return false;
                string mode = header.Substring(0, firstSpace);
                string objectId = header.Substring(firstSpace + 1, lastSpace - firstSpace - 1);
                if (indexedTree.ContainsKey(relative)) return false;
                indexedTree.Add(relative, mode + " " + objectId);
            }

            if (allowMetadataOverlay)
            {
                foreach (KeyValuePair<string, string> indexedEntry in indexedTree)
                {
                    if (!indexedEntry.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!projectionFiles.ContainsKey(indexedEntry.Key)) return false;
                    if (expectedTree.ContainsKey(indexedEntry.Key)) continue;
                    expectedTree.Add(indexedEntry.Key, string.Empty);
                }
                foreach (KeyValuePair<string, string> projectionFile in projectionFiles)
                {
                    if (!projectionFile.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    if (indexedTree.ContainsKey(projectionFile.Key)) continue;
                    string projectPath = PackageRelativePath.Normalize(projectionRelative + "/" + projectionFile.Key);
                    if (!ProjectPathIgnored(gitRoot, projectPath)) return false;
                }
            }

            if (indexedTree.Count != expectedTree.Count) return false;
            foreach (KeyValuePair<string, string> expectedEntry in expectedTree)
            {
                string indexedValue;
                if (!indexedTree.TryGetValue(expectedEntry.Key, out indexedValue)) return false;
                bool expectedValueProvided = !string.IsNullOrWhiteSpace(expectedEntry.Value);
                bool indexedValueMatches = indexedValue.Equals(expectedEntry.Value, StringComparison.OrdinalIgnoreCase);
                if (expectedValueProvided && !indexedValueMatches) return false;
                string indexedRelative = PackageRelativePath.Normalize(projectionRelative + "/" + expectedEntry.Key);
                string platformRelative = expectedEntry.Key.Replace('/', Path.DirectorySeparatorChar);
                string projectionFile = Path.Combine(projection, platformRelative);
                string requiredMode = expectedEntry.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? "100644" : string.Empty;
                bool projectionFileExists = File.Exists(projectionFile);
                bool indexFileMatches;
                if (junctionAllowed)
                    indexFileMatches = IndexFileMatchesThroughVerifiedJunction(gitRoot, indexedRelative, projectionFile, projectionFileExists, requiredMode, allowedJunction, expectedJunctionTarget);
                else
                    indexFileMatches = IndexFileMatchesWorktree(gitRoot, indexedRelative, projectionFileExists, requiredMode);
                if (!indexFileMatches) return false;
                if (!expectedEntry.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || allowMetadataOverlay) continue;
                string sourceMetadata = Path.Combine(source, platformRelative);
                if (!File.Exists(sourceMetadata)) return false;
                string sourceMetadataHash = IO.HashFile(sourceMetadata);
                string projectionMetadataHash = IO.HashFile(projectionFile);
                if (!sourceMetadataHash.Equals(projectionMetadataHash, StringComparison.Ordinal)) return false;
            }
            bool metaExists = File.Exists(projection + ".meta");
            return IndexFileMatchesWorktree(gitRoot, projectionRelative + ".meta", metaExists, "100644");
        }

        internal static bool ConversionMetadataMatches(string gitRoot, string source, string projection, string projectionRelative)
        {
            bool sourceExists = Directory.Exists(source);
            bool projectionExists = Directory.Exists(projection);
            if (!sourceExists || !projectionExists) return false;
            Dictionary<string, string> sourceTree = IO.GitTree(source);
            SortedDictionary<string, string> sourceFiles = IO.Files(source);
            SortedDictionary<string, string> projectionFiles = IO.Files(projection);
            return ConversionMetadataMatches(gitRoot, source, projection, projectionRelative, sourceTree, sourceFiles, projectionFiles);
        }

        private static bool ConversionMetadataMatches(string gitRoot, string source, string projection, string projectionRelative, IReadOnlyDictionary<string, string> sourceTree, IReadOnlyDictionary<string, string> sourceFiles, IReadOnlyDictionary<string, string> projectionFiles)
        {
            foreach (KeyValuePair<string, string> projectionFile in projectionFiles)
            {
                if (!projectionFile.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (!MetadataCompanionExists(source, projectionFile.Key)) return false;
                string metadataRelative = projectionFile.Key.Replace('/', Path.DirectorySeparatorChar);
                string sourceMetadata = Path.Combine(source, metadataRelative);
                if (sourceTree.ContainsKey(projectionFile.Key))
                {
                    if (!File.Exists(sourceMetadata)) return false;
                    string sourceMetadataHash = IO.HashFile(sourceMetadata);
                    string projectionMetadataHash = IO.HashFile(projectionFile.Value);
                    if (!sourceMetadataHash.Equals(projectionMetadataHash, StringComparison.Ordinal)) return false;
                    continue;
                }
                if (!SourcePathIgnored(source, projectionFile.Key)) return false;
                if (File.Exists(sourceMetadata))
                {
                    string sourceMetadataHash = IO.HashFile(sourceMetadata);
                    string projectionMetadataHash = IO.HashFile(projectionFile.Value);
                    if (!sourceMetadataHash.Equals(projectionMetadataHash, StringComparison.Ordinal)) return false;
                }
            }
            foreach (KeyValuePair<string, string> sourceFile in sourceFiles)
            {
                if (!sourceFile.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (sourceTree.ContainsKey(sourceFile.Key)) continue;
                if (IsPairedGeneratedMetadataExclusion(gitRoot, source, projectionRelative, sourceTree, sourceFile.Key)) continue;
                string projectionMetadata;
                if (!projectionFiles.TryGetValue(sourceFile.Key, out projectionMetadata)) return false;
                string sourceMetadataHash = IO.HashFile(sourceFile.Value);
                string projectionMetadataHash = IO.HashFile(projectionMetadata);
                if (!sourceMetadataHash.Equals(projectionMetadataHash, StringComparison.Ordinal)) return false;
            }
            foreach (KeyValuePair<string, string> sourceEntry in sourceTree)
            {
                if (!sourceEntry.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string projectionMetadata;
                if (!projectionFiles.TryGetValue(sourceEntry.Key, out projectionMetadata)) return false;
                string platformRelative = sourceEntry.Key.Replace('/', Path.DirectorySeparatorChar);
                string sourceMetadata = Path.Combine(source, platformRelative);
                string sourceMetadataHash = IO.HashFile(sourceMetadata);
                string projectionMetadataHash = IO.HashFile(projectionMetadata);
                if (!sourceMetadataHash.Equals(projectionMetadataHash, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        internal static bool IndexEntryMatchesSource(string gitRoot, string relative, string sourceTreeEntry)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            string entries = IO.Git(gitRoot, "ls-files", "--stage", "-z", "--", normalized);
            string[] records = entries.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            if (records.Length != 1) return false;
            string expected = sourceTreeEntry + " 0\t" + normalized;
            if (!records[0].Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
            return IndexFileMatchesWorktree(gitRoot, normalized, true);
        }

        private static bool IndexEntryMatchesSourceThroughVerifiedJunction(string gitRoot, string relative, string sourceTreeEntry, string absolute, string allowedJunction, string expectedJunctionTarget)
        {
            string normalized = PackageRelativePath.Normalize(relative);
            string entries = IO.Git(gitRoot, "ls-files", "--stage", "-z", "--", normalized);
            string[] records = entries.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            if (records.Length != 1) return false;
            string expected = sourceTreeEntry + " 0\t" + normalized;
            if (!records[0].Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
            return IndexFileMatchesThroughVerifiedJunction(gitRoot, normalized, absolute, true, string.Empty, allowedJunction, expectedJunctionTarget);
        }

        internal static bool IsPairedGeneratedMetadataExclusion(string gitRoot, string source, string projectionRelative, IReadOnlyDictionary<string, string> sourceTree, string metadataRelative)
        {
            return IsPairedGeneratedMetadataExclusion(gitRoot, source, projectionRelative, sourceTree, metadataRelative, string.Empty, string.Empty);
        }

        private static bool IsPairedGeneratedMetadataExclusion(string gitRoot, string source, string projectionRelative, IReadOnlyDictionary<string, string> sourceTree, string metadataRelative, string allowedJunction, string expectedJunctionTarget)
        {
            if (!metadataRelative.EndsWith(".csproj.meta", StringComparison.OrdinalIgnoreCase)) return false;
            bool trackedMetadata = sourceTree.ContainsKey(metadataRelative);
            string companionRelative = metadataRelative.Substring(0, metadataRelative.Length - ".meta".Length);
            string sourceTreeEntry;
            if (!sourceTree.TryGetValue(companionRelative, out sourceTreeEntry)) return false;
            if (!IsSafeGeneratedProjectionExclusion(companionRelative)) return false;
            string companionProjectPath = PackageRelativePath.Normalize(projectionRelative + "/" + companionRelative);
            if (!ProjectPathIgnored(gitRoot, companionProjectPath)) return false;
            bool junctionAllowed = !string.IsNullOrWhiteSpace(allowedJunction) && !string.IsNullOrWhiteSpace(expectedJunctionTarget);
            bool companionPreserved;
            if (junctionAllowed)
            {
                string companionPlatformRelative = companionRelative.Replace('/', Path.DirectorySeparatorChar);
                string companionAbsolute = Path.Combine(allowedJunction, companionPlatformRelative);
                companionPreserved = IndexEntryMatchesSourceThroughVerifiedJunction(gitRoot, companionProjectPath, sourceTreeEntry, companionAbsolute, allowedJunction, expectedJunctionTarget);
            }
            else
            {
                companionPreserved = IndexEntryMatchesSource(gitRoot, companionProjectPath, sourceTreeEntry);
            }
            if (companionPreserved) return false;
            string platformRelative = metadataRelative.Replace('/', Path.DirectorySeparatorChar);
            string metadataPath = Path.Combine(source, platformRelative);
            bool metadataExists = File.Exists(metadataPath);
            bool metadataReparse = IO.Reparse(metadataPath);
            if (!metadataExists || metadataReparse) return false;
            if (trackedMetadata) return true;
            return SourcePathIgnored(source, metadataRelative);
        }

        internal static bool SourcePathIgnored(string source, string relative)
        {
            try
            {
                string normalized = PackageRelativePath.Normalize(relative);
                IO.Git(source, "check-ignore", "--no-index", "--quiet", "--", normalized);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        internal static bool MetadataCompanionExists(string source, string metadataRelative)
        {
            if (!metadataRelative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return false;
            string companionRelative = metadataRelative.Substring(0, metadataRelative.Length - ".meta".Length);
            string platformRelative = companionRelative.Replace('/', Path.DirectorySeparatorChar);
            string companion = Path.Combine(source, platformRelative);
            bool companionFileExists = File.Exists(companion);
            bool companionDirectoryExists = Directory.Exists(companion);
            return companionFileExists || companionDirectoryExists;
        }

        internal static string FileInventoryFingerprint(string root)
        {
            SortedDictionary<string, string> files = IO.Files(root);
            StringBuilder evidence = new StringBuilder();
            foreach (KeyValuePair<string, string> file in files)
            {
                string fingerprint = IO.HashFile(file.Value);
                evidence.Append(file.Key);
                evidence.Append('\n');
                evidence.Append(fingerprint);
                evidence.Append('\n');
            }
            using (SHA256 hash = SHA256.Create())
            {
                string evidenceText = evidence.ToString();
                byte[] evidenceBytes = Encoding.UTF8.GetBytes(evidenceText);
                byte[] fingerprintBytes = hash.ComputeHash(evidenceBytes);
                return IO.Hex(fingerprintBytes);
            }
        }

        internal static bool WorkingTreeMatchesHead(string checkout)
        {
            if (!Directory.Exists(checkout)) return false;
            Dictionary<string, string> tree = IO.GitTree(checkout);
            foreach (KeyValuePair<string, string> entry in tree)
            {
                bool ordinaryFileMode = entry.Value.StartsWith("100644 ", StringComparison.Ordinal);
                bool executableFileMode = entry.Value.StartsWith("100755 ", StringComparison.Ordinal);
                bool ordinaryMode = ordinaryFileMode || executableFileMode;
                if (!ordinaryMode) return false;
                string platformRelative = entry.Key.Replace('/', Path.DirectorySeparatorChar);
                string file = Path.Combine(checkout, platformRelative);
                bool fileExists = File.Exists(file);
                bool fileReparse = IO.Reparse(file);
                if (!fileExists || fileReparse) return false;
                string expectedObjectId = entry.Value.Substring(7);
                string actualObjectId = IO.Git(checkout, "hash-object", "--path=" + entry.Key, "--", file);
                if (!actualObjectId.Equals(expectedObjectId, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }

    internal sealed class WindowsCreatePackageSetEnvironment : ICreatePackageSetEnvironment
    {
        private readonly string helperPath;
        private readonly string unityExecutable;

        internal WindowsCreatePackageSetEnvironment(string helperPath, string unityExecutable)
        {
            this.helperPath = helperPath;
            this.unityExecutable = unityExecutable;
        }

        public ProjectWorkflowObservation Observe(CreatePackageSetRequest request)
        {
            string projectGitRoot;
            string projectGitDirectory;
            return WindowsPackageSetEnvironmentSupport.ObserveProject(request.ProjectRoot, out projectGitRoot, out projectGitDirectory);
        }

        public IReadOnlyList<string> Validate(CreatePackageSetRequest request, ProjectWorkflowObservation observation)
        {
            IReadOnlyList<string> commonConflicts = WindowsPackageSetEnvironmentSupport.ValidateCommon(request.ProjectRoot, request.PackageSetRoot, helperPath, true);
            List<string> conflicts = new List<string>(commonConflicts);
            try
            {
                bool repositoryProvided = !string.IsNullOrWhiteSpace(request.PackageSetRepository);
                if (repositoryProvided) IO.RequireRemote(request.PackageSetRepository);
            }
            catch (Exception error)
            {
                conflicts.Add("PackageSet repository is invalid: " + error.Message);
            }
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
            try
            {
                WindowsPackageSetEnvironmentSupport.RequireOrdinaryAssetsProjectionRoot(request.ProjectRoot, request.ProjectionRoot);
            }
            catch (Exception error)
            {
                conflicts.Add("Managed projection root is invalid: " + error.Message);
            }
            try
            {
                string discoveredProjectGitRoot = IO.Git(request.ProjectRoot, "rev-parse", "--show-toplevel");
                string projectGitRoot = IO.Full(discoveredProjectGitRoot);
                string projectRepository = WindowsPackageSetEnvironmentSupport.ProjectRemote(projectGitRoot);
                if (string.IsNullOrWhiteSpace(projectRepository)) conflicts.Add("Create PackageSet requires a Project origin repository identity.");
            }
            catch (Exception error)
            {
                conflicts.Add("Project origin repository is invalid: " + error.Message);
            }
            return conflicts;
        }

        public PackageSetWorkspace Acquire(CreatePackageSetRequest request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string packageSetRoot = IO.Full(request.PackageSetRoot);
            if (IO.Exists(packageSetRoot))
            {
                IEnumerator<string>? enumerator = null;
                bool emptyDirectory = false;
                if (Directory.Exists(packageSetRoot))
                {
                    IEnumerable<string> entries = Directory.EnumerateFileSystemEntries(packageSetRoot);
                    enumerator = entries.GetEnumerator();
                    emptyDirectory = !enumerator.MoveNext();
                    enumerator.Dispose();
                }
                if (!emptyDirectory) throw new InvalidOperationException("Create PackageSet will not overwrite an existing destination.");
            }
            string? parent = Path.GetDirectoryName(packageSetRoot);
            bool parentMissing = string.IsNullOrWhiteSpace(parent);
            if (parentMissing) throw new InvalidOperationException("PackageSet destination has no parent directory.");
            Directory.CreateDirectory(parent!);
            Directory.CreateDirectory(packageSetRoot);
            bool fullTrackingRef = request.TrackingRef.StartsWith("refs/", StringComparison.Ordinal);
            if (fullTrackingRef) IO.Git(packageSetRoot, "init");
            else IO.Git(packageSetRoot, "init", "-b", request.TrackingRef);
            IO.Git(packageSetRoot, "var", "GIT_AUTHOR_IDENT");
            bool repositoryProvided = !string.IsNullOrWhiteSpace(request.PackageSetRepository);
            if (repositoryProvided) IO.Git(packageSetRoot, "remote", "add", "origin", request.PackageSetRepository);
            cancellation.ThrowIfCancellationRequested();
            return new PackageSetWorkspace(packageSetRoot);
        }

        public ObservedCreatePackageSetSource ObserveSelection(CreatePackageSetRequest request, PackageSetWorkspace workspace, PackagePlacement placement)
        {
            try
            {
                WindowsPackageSetEnvironmentSupport.RequireAssetsProjectionRoot(placement.ProjectionPath);
                string original = IO.Under(request.ProjectRoot, placement.OriginalProjectPath);
                string projection = IO.Under(request.ProjectRoot, placement.ProjectionPath);
                string assetsPath = Path.Combine(request.ProjectRoot, "Assets");
                string assetsRoot = IO.Full(assetsPath);
                bool originalOwned = IO.Within(assetsRoot, original);
                bool projectionOwned = IO.Within(assetsRoot, projection);
                bool pathOwned = originalOwned && projectionOwned;
                string originalMetadata = original + ".meta";
                bool metadataOccupied = IO.Exists(originalMetadata);
                bool metadataOwned = !metadataOccupied;
                if (metadataOccupied) metadataOwned = File.Exists(originalMetadata);
                bool differentProjection = !original.Equals(projection, StringComparison.OrdinalIgnoreCase);
                bool projectionPathOccupied = IO.Exists(projection);
                bool projectionMetadataOccupied = IO.Exists(projection + ".meta");
                bool occupiedProjection = differentProjection && (projectionPathOccupied || projectionMetadataOccupied);
                bool originalDirectoryExists = Directory.Exists(original);
                bool originalReparse = false;
                if (originalDirectoryExists) originalReparse = IO.Reparse(original);
                bool physicalSource = originalDirectoryExists && !originalReparse;
                if (!pathOwned || !metadataOwned || occupiedProjection || !physicalSource)
                    return ConflictSource(string.Empty, metadataOwned, pathOwned, "create-selection-path-conflict");

                if (placement.Kind == DevelopmentSourceKind.LocalDraft)
                {
                    string gitMetadataPath = Path.Combine(original, ".git");
                    bool gitMetadataExists = IO.Exists(gitMetadataPath);
                    if (gitMetadataExists)
                    {
                        return ConflictSource(string.Empty, metadataOwned, pathOwned, "local-draft-has-git-metadata");
                    }
                    string contentHash = IO.HashTree(original);
                    string discoveredDraftProjectGitRoot = IO.Git(request.ProjectRoot, "rev-parse", "--show-toplevel");
                    string draftProjectGitRoot = IO.Full(discoveredDraftProjectGitRoot);
                    string projectRelative = IO.Relative(draftProjectGitRoot, original);
                    bool draftIndexAligned = WindowsPackageSetEnvironmentSupport.OrdinaryProjectionIndexMatches(draftProjectGitRoot, original, original, projectRelative, false);
                    DevelopmentSourceObservation draftObservation = new DevelopmentSourceObservation(true, contentHash, string.Empty, false, true, false, metadataOwned, pathOwned, draftIndexAligned, DevelopmentProjectionState.PhysicalSnapshot, contentHash);
                    return new ObservedCreatePackageSetSource(string.Empty, draftObservation);
                }

                if (!WindowsPackageSetEnvironmentSupport.IsIndependentGitCheckout(original))
                    return ConflictSource(string.Empty, metadataOwned, pathOwned, "git-source-is-not-independent");
                string repository = IO.Git(original, "remote", "get-url", "origin");
                IO.RequireRemote(repository);
                string head = IO.Git(original, "rev-parse", "HEAD");
                string discoveredProjectGitRoot = IO.Git(request.ProjectRoot, "rev-parse", "--show-toplevel");
                string projectGitRoot = IO.Full(discoveredProjectGitRoot);
                string relative = IO.Relative(projectGitRoot, original);
                string exactPin = IO.Gitlink(projectGitRoot, relative);
                if (string.IsNullOrWhiteSpace(exactPin) && placement.BootstrapRuntime) exactPin = head;
                bool dirty = IO.Git(original, "status", "--porcelain").Length != 0;
                bool exactPinProvided = !string.IsNullOrWhiteSpace(exactPin);
                bool aligned = exactPinProvided && head.Equals(exactPin, StringComparison.OrdinalIgnoreCase);
                string evidence = IO.HashTree(original);
                bool gitlinkIndexAligned = WindowsPackageSetEnvironmentSupport.IndexGitlinkMatches(projectGitRoot, relative, exactPin);
                bool originalMetaExists = File.Exists(original + ".meta");
                bool originalMetaAligned = WindowsPackageSetEnvironmentSupport.IndexFileMatchesWorktree(projectGitRoot, relative + ".meta", originalMetaExists, "100644");
                bool indexAligned = gitlinkIndexAligned && originalMetaAligned;
                DevelopmentSourceObservation observation = new DevelopmentSourceObservation(true, head, exactPin, dirty, aligned, !aligned, metadataOwned, pathOwned, indexAligned, DevelopmentProjectionState.BootstrapGitlink, evidence);
                return new ObservedCreatePackageSetSource(repository, observation);
            }
            catch (Exception error)
            {
                return ConflictSource(string.Empty, false, false, error.Message);
            }
        }

        public PackageSetResolution PreparePackageSet(CreatePackageSetPlan plan, CancellationToken cancellation)
        {
            foreach (PackageSetEntryPlan entryPlan in plan.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                PrepareSource(plan, entryPlan, cancellation);
            }

            PackageSetDocument manifest = BuildManifest(plan);
            string checkoutPath = plan.Workspace.CheckoutPath;
            string manifestPath = Path.Combine(checkoutPath, "package-set.json");
            string manifestJson = JsonUtility.ToJson(manifest, true) + "\n";
            UTF8Encoding manifestEncoding = new UTF8Encoding(false);
            File.WriteAllText(manifestPath, manifestJson, manifestEncoding);
            IO.Git(checkoutPath, "add", "--", "package-set.json");
            string gitmodulesPath = Path.Combine(checkoutPath, ".gitmodules");
            bool gitmodulesExists = File.Exists(gitmodulesPath);
            if (gitmodulesExists) IO.Git(checkoutPath, "add", "--", ".gitmodules");
            string gitignorePath = Path.Combine(checkoutPath, ".gitignore");
            bool gitignoreExists = File.Exists(gitignorePath);
            if (gitignoreExists) IO.Git(checkoutPath, "add", "--", ".gitignore");
            foreach (PackagePlacement placement in plan.Layout.Placements)
            {
                if (placement.Kind == DevelopmentSourceKind.GitSubmodule) IO.Git(checkoutPath, "add", "--", placement.SourcePath);
            }
            IO.Git(checkoutPath, "commit", "-m", "Initialize PackageSet source composition");
            string exactRevision = IO.Git(checkoutPath, "rev-parse", "HEAD");
            string status = IO.Git(checkoutPath, "status", "--porcelain");
            if (status.Length != 0)
                throw new InvalidOperationException("New PackageSet is dirty after preparation.");
            string repository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(checkoutPath);
            return new PackageSetResolution(checkoutPath, repository, exactRevision);
        }

        public string ExportOfflineApply(CreatePackageSetPlan plan, PackageSetResolution resolution)
        {
            string projectGitRoot;
            string projectGitDirectory;
            WindowsPackageSetEnvironmentSupport.ResolveProjectGitLocations(plan.Request.ProjectRoot, out projectGitRoot, out projectGitDirectory);
            return WindowsOfflineApplyExporter.ExportCreate(plan, resolution, projectGitRoot, projectGitDirectory, helperPath, unityExecutable);
        }

        public void MarkOfflineApplyReady(string applyInstructionsPath)
        {
            WindowsOfflineApplyExporter.MarkReady(applyInstructionsPath);
        }

        private static ObservedCreatePackageSetSource ConflictSource(string repository, bool metadataOwned, bool pathOwned, string evidence)
        {
            DevelopmentSourceObservation observation = new DevelopmentSourceObservation(false, string.Empty, string.Empty, false, false, true, metadataOwned, pathOwned, false, DevelopmentProjectionState.Conflict, evidence);
            return new ObservedCreatePackageSetSource(repository, observation);
        }

        private void PrepareSource(CreatePackageSetPlan plan, PackageSetEntryPlan entryPlan, CancellationToken cancellation)
        {
            PackagePlacement placement = entryPlan.Placement;
            string original = IO.Under(plan.Request.ProjectRoot, placement.OriginalProjectPath);
            string source = IO.Under(plan.Workspace.CheckoutPath, placement.SourcePath);
            bool sourceOccupied = IO.Exists(source);
            if (sourceOccupied) throw new InvalidOperationException("Canonical source destination is occupied: " + source);
            string? parent = Path.GetDirectoryName(source);
            bool parentMissing = string.IsNullOrWhiteSpace(parent);
            if (parentMissing) throw new InvalidOperationException("Canonical source has no parent: " + source);
            Directory.CreateDirectory(parent!);

            if (placement.Kind == DevelopmentSourceKind.GitSubmodule)
            {
                string remote = IO.Git(original, "remote", "get-url", "origin");
                IO.RequireRemote(remote);
                if (!remote.Equals(entryPlan.SourceRepository, StringComparison.Ordinal))
                    throw new InvalidOperationException("Selected source repository changed after observation: " + placement.Identity);
                string currentHead = IO.Git(original, "rev-parse", "HEAD");
                if (!currentHead.Equals(entryPlan.Observation.SourceHead, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Selected source HEAD changed after observation: " + placement.Identity);
                string currentStatus = IO.Git(original, "status", "--porcelain");
                if (currentStatus.Length != 0)
                    throw new InvalidOperationException("Selected source became dirty after observation: " + placement.Identity);
                string currentEvidence = IO.HashTree(original);
                if (!currentEvidence.Equals(entryPlan.Observation.EvidenceToken, StringComparison.Ordinal))
                    throw new InvalidOperationException("Selected source content changed after observation: " + placement.Identity);
                string[] addArguments = { "submodule", "add", "--name", placement.Identity, "--", remote, placement.SourcePath };
                IO.Run("git", plan.Workspace.CheckoutPath, addArguments, cancellation);
                string exactPin = entryPlan.Observation.ExactPin;
                try
                {
                    IO.Git(source, "cat-file", "-e", exactPin + "^{commit}");
                }
                catch (InvalidOperationException)
                {
                    string[] fetchArguments = { "fetch", "--no-tags", "origin", exactPin };
                    IO.Run("git", source, fetchArguments, cancellation);
                }
                IO.Git(source, "checkout", "--detach", exactPin);
            }
            else
            {
                string currentEvidence = IO.HashTree(original);
                if (!currentEvidence.Equals(entryPlan.Observation.EvidenceToken, StringComparison.Ordinal))
                    throw new InvalidOperationException("Selected LocalDraft content changed after observation: " + placement.Identity);
                IO.CopyTree(original, source);
                string checkoutPath = plan.Workspace.CheckoutPath;
                string discoveredSetGitDirectory = IO.Git(checkoutPath, "rev-parse", "--absolute-git-dir");
                string setGitDirectory = IO.Full(discoveredSetGitDirectory);
                string draftGitDirectory = Path.Combine(setGitDirectory, "local-drafts", placement.Identity);
                string? draftParent = Path.GetDirectoryName(draftGitDirectory);
                bool draftParentMissing = string.IsNullOrWhiteSpace(draftParent);
                if (draftParentMissing) throw new InvalidOperationException("Local draft Git directory has no parent.");
                Directory.CreateDirectory(draftParent!);
                IO.Git(checkoutPath, "init", "--separate-git-dir=" + draftGitDirectory, source);
                IO.Git(source, "var", "GIT_AUTHOR_IDENT");
                IO.Git(source, "add", "--all", "--", ".");
                IO.Git(source, "commit", "-m", "Initialize " + placement.Identity + " from explicit Project selection");
                string escapedSourcePath = WindowsPackageSetEnvironmentSupport.EscapeIgnore(placement.SourcePath);
                string ignoreRecord = "\n/" + escapedSourcePath + "/\n";
                string ignorePath = Path.Combine(checkoutPath, ".gitignore");
                UTF8Encoding ignoreEncoding = new UTF8Encoding(false);
                File.AppendAllText(ignorePath, ignoreRecord, ignoreEncoding);
            }

            if (!WindowsPackageSetEnvironmentSupport.IsIndependentGitCheckout(source))
                throw new InvalidOperationException("Canonical source is not an independent Git checkout: " + placement.Identity);
            string discoveredSourceGitDirectory = IO.Git(source, "rev-parse", "--absolute-git-dir");
            string sourceGitDirectory = IO.Full(discoveredSourceGitDirectory);
            string discoveredPackageSetGitDirectory = IO.Git(plan.Workspace.CheckoutPath, "rev-parse", "--absolute-git-dir");
            string packageSetGitDirectory = IO.Full(discoveredPackageSetGitDirectory);
            if (!IO.Within(packageSetGitDirectory, sourceGitDirectory))
                throw new InvalidOperationException("Canonical source Git metadata does not belong to the new PackageSet: " + placement.Identity);
            IO.GitTree(source);
            WindowsPackageSetEnvironmentSupport.PreserveMetadata(original, source);
            string sourceStatus = IO.Git(source, "status", "--porcelain");
            if (sourceStatus.Length != 0)
                throw new InvalidOperationException("Canonical source is dirty after preparation: " + placement.Identity);
            string ignoredOutput = IO.Git(source, "ls-files", "--others", "--ignored", "--exclude-standard", "-z");
            string[] ignoredPaths = ignoredOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string ignoredPath in ignoredPaths)
            {
                if (!ignoredPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Ignored non-metadata file would leak through backing: " + ignoredPath);
            }
        }

        private PackageSetDocument BuildManifest(CreatePackageSetPlan plan)
        {
            PackageSetDocument manifest = new PackageSetDocument();
            manifest.schemaVersion = 2;
            manifest.identity = plan.Request.PackageSetIdentity;
            manifest.sourceRoot = plan.Layout.SourceRoot;
            string projectionRoot = WindowsPackageSetEnvironmentSupport.RequireOrdinaryAssetsProjectionRoot(plan.Request.ProjectRoot, plan.Layout.ProjectionRoot);
            manifest.managedProjectionRoot = projectionRoot;
            manifest.target = new PackageSetTargetDocument();
            DirectoryInfo projectDirectory = new DirectoryInfo(plan.Request.ProjectRoot);
            manifest.target.identity = "Project." + projectDirectory.Name;
            string discoveredProjectGitRoot = IO.Git(plan.Request.ProjectRoot, "rev-parse", "--show-toplevel");
            string projectGitRoot = IO.Full(discoveredProjectGitRoot);
            string projectRepository = WindowsPackageSetEnvironmentSupport.ProjectRemote(projectGitRoot);
            if (string.IsNullOrWhiteSpace(projectRepository)) throw new InvalidOperationException("Create PackageSet requires a Project origin repository identity.");
            manifest.target.repository = projectRepository;
            List<PackageSetEntryDocument> entries = new List<PackageSetEntryDocument>();
            foreach (PackagePlacement placement in plan.Layout.Placements)
            {
                PackageSetEntryDocument entry = new PackageSetEntryDocument();
                entry.identity = placement.Identity;
                entry.sourcePath = placement.SourcePath;
                string normalizedPlacementProjection = PackageRelativePath.Normalize(placement.ProjectionPath);
                string projectionPrefix = projectionRoot + "/";
                if (!normalizedPlacementProjection.StartsWith(projectionPrefix, StringComparison.Ordinal))
                    throw new InvalidOperationException("Create PackageSet placement is outside the managed Assets projection root: " + placement.Identity);
                entry.projectionPath = normalizedPlacementProjection.Substring(projectionPrefix.Length);
                entry.kind = placement.Kind.ToString();
                entry.bootstrapRuntime = WindowsPackageSetEnvironmentSupport.IsRuntimeIdentity(placement.Identity);
                entries.Add(entry);
            }
            manifest.entries = entries.ToArray();
            return manifest;
        }
    }

    internal static class WindowsOfflineApplyExporter
    {
        internal static void MarkReady(string applyInstructionsPath)
        {
            string planPath = IO.Full(applyInstructionsPath);
            string? operation = Path.GetDirectoryName(planPath);
            bool operationMissing = string.IsNullOrWhiteSpace(operation);
            if (operationMissing) throw new InvalidOperationException("Offline Apply plan has no operation directory.");
            string operationPath = operation!;
            RequireOrdinaryOperationDirectory(operationPath);
            IO.NoReparseAncestors(planPath);
            bool regularPlan = WindowsPackageSetEnvironmentSupport.IsRegularDurableFile(planPath);
            if (!regularPlan) throw new InvalidOperationException("Offline Apply plan must be an ordinary file: " + planPath);
            string readyPath = Path.Combine(operationPath, "ready.sha256");
            WindowsPackageSetEnvironmentSupport.DurableFileSnapshot planSnapshot = WindowsPackageSetEnvironmentSupport.OpenDurableFileSnapshot(planPath);
            using (planSnapshot)
            {
                string planHash = planSnapshot.Fingerprint;
                WriteNewOperationText(readyPath, planHash);
            }
        }

        internal static string ExportCreate(CreatePackageSetPlan plan, PackageSetResolution resolution, string gitRoot, string gitDirectory, string helperPath, string unityExecutable)
        {
            string projectBindingPath = IO.Under(gitRoot, "PackageManagement/project.packageset.json");
            string runtimeBindingsPath = IO.Under(gitRoot, "PackageManagement/runtime.bindings.json");
            string projectBindingFingerprint = IO.HashFile(projectBindingPath);
            string runtimeBindingsFingerprint = IO.HashFile(runtimeBindingsPath);
            return Export(plan.Request.ProjectRoot, plan.Request.PackageSetRepository, plan.Request.TrackingRef, plan.Layout.ProjectionRoot, plan.Entries, plan.NeedsPublish, resolution, plan.ProjectObservation, gitRoot, gitDirectory, helperPath, unityExecutable, true, true, projectBindingFingerprint, runtimeBindingsFingerprint);
        }

        internal static string ExportExisting(UseExistingPackageSetPlan plan, string gitRoot, string gitDirectory, string helperPath, string unityExecutable)
        {
            bool needsPublish = string.IsNullOrWhiteSpace(plan.Resolution.Repository);
            foreach (PackageSetEntryPlan entry in plan.Entries)
            {
                if (entry.Assessment.NeedsPublish) needsPublish = true;
            }
            string projectBindingFingerprint = plan.PackageSetModeObservation.ProjectBinding.ContentFingerprint;
            string runtimeBindingsFingerprint = plan.PackageSetModeObservation.RuntimeBindings.ContentFingerprint;
            return Export(plan.Request.ProjectRoot, plan.Resolution.Repository, plan.Request.TrackingRef, plan.Layout.ProjectionRoot, plan.Entries, needsPublish, plan.Resolution, plan.ProjectObservation, gitRoot, gitDirectory, helperPath, unityExecutable, plan.ProjectBindingRequiresWrite, plan.RuntimeBindingsRequireWrite, projectBindingFingerprint, runtimeBindingsFingerprint);
        }

        private static string Export(string projectRoot, string repository, string trackingRef, string projectionRoot, IReadOnlyList<PackageSetEntryPlan> entryPlans, bool needsPublish, PackageSetResolution resolution, ProjectWorkflowObservation projectObservation, string gitRoot, string gitDirectory, string helperPath, string unityExecutable, bool projectBindingRequiresWrite, bool runtimeBindingsRequireWrite, string projectBindingFingerprint, string runtimeBindingsFingerprint)
        {
            string operationRoot = Path.Combine(gitDirectory, "PackageManagement", "operations");
            IO.NoReparseAncestors(operationRoot, true);
            Directory.CreateDirectory(operationRoot);
            RequireOrdinaryOperationDirectory(operationRoot);
            Guid operationIdentifier = Guid.NewGuid();
            string operationId = operationIdentifier.ToString("N");
            string operation = Path.Combine(operationRoot, operationId);
            IO.NoReparseAncestors(operation);
            bool operationOccupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(operation);
            if (operationOccupied) throw new InvalidOperationException("Offline Apply operation path is already occupied: " + operation);
            Directory.CreateDirectory(operation);
            RequireOrdinaryOperationDirectory(operation);

            List<OfflineApplyEntry> entries = new List<OfflineApplyEntry>();
            foreach (PackageSetEntryPlan entryPlan in entryPlans)
            {
                OfflineApplyEntry entry = BuildEntry(projectRoot, resolution.CheckoutPath, gitRoot, entryPlan);
                entries.Add(entry);
            }

            ProjectPackageSetDocument binding = new ProjectPackageSetDocument();
            binding.schemaVersion = 3;
            binding.repository = repository;
            binding.trackingRef = string.IsNullOrWhiteSpace(trackingRef) ? "main" : trackingRef;
            binding.resolvedRevision = resolution.ExactRevision;
            binding.managedProjectionRoot = projectionRoot;
            binding.needsPublish = needsPublish;

            List<OfflineWrite> writes = new List<OfflineWrite>();
            if (projectBindingRequiresWrite) AddWrite(gitRoot, writes, "PackageManagement/project.packageset.json", binding, projectBindingFingerprint);
            List<PackagePlacement> runtime = new List<PackagePlacement>();
            foreach (PackageSetEntryPlan entryPlan in entryPlans)
            {
                PackagePlacement placement = entryPlan.Placement;
                bool runtimeIdentity = WindowsPackageSetEnvironmentSupport.IsRuntimeIdentity(placement.Identity);
                if (runtimeIdentity) runtime.Add(placement);
            }
            if (runtime.Count != 0 && runtime.Count != 3)
                throw new InvalidOperationException("Package Manager runtime composition must contain zero or all three canonical entries.");
            if (runtime.Count == 3 && runtimeBindingsRequireWrite) AddRuntimeBindings(projectRoot, resolution.CheckoutPath, gitRoot, writes, runtime, runtimeBindingsFingerprint);

            OfflineApplyDocument apply = new OfflineApplyDocument();
            apply.schemaVersion = 2;
            apply.projectRoot = IO.Full(projectRoot);
            apply.projectGitRoot = gitRoot;
            apply.projectGitDirectory = gitDirectory;
            apply.packageSetRoot = IO.Full(resolution.CheckoutPath);
            apply.packageSetRevision = resolution.ExactRevision;
            string currentProjectHead = IO.Git(gitRoot, "rev-parse", "HEAD");
            string indexPath = Path.Combine(gitDirectory, "index");
            string currentIndexFingerprint = IO.HashFile(indexPath);
            bool projectHeadChanged = !currentProjectHead.Equals(projectObservation.ProjectHead, StringComparison.OrdinalIgnoreCase);
            if (projectHeadChanged) throw new InvalidOperationException("Project HEAD changed after workflow observation.");
            bool indexChanged = !currentIndexFingerprint.Equals(projectObservation.IndexFingerprint, StringComparison.Ordinal);
            if (indexChanged) throw new InvalidOperationException("Project Git index changed after workflow observation.");
            apply.projectHead = projectObservation.ProjectHead;
            apply.indexHash = projectObservation.IndexFingerprint;
            string gitmodulesPath = Path.Combine(gitRoot, ".gitmodules");
            apply.gitmodulesHash = IO.HashFile(gitmodulesPath);
            apply.projectBindingHash = projectBindingFingerprint;
            apply.runtimeBindingsHash = runtimeBindingsFingerprint;
            apply.unityExecutable = unityExecutable;
            apply.entries = entries.ToArray();
            apply.writes = writes.ToArray();

            string planPath = Path.Combine(operation, "plan.json");
            string planJson = JsonUtility.ToJson(apply, true);
            WriteNewOperationText(planPath, planJson);
            string helperCopy = Path.Combine(operation, "ApplyDevelopmentBootstrap.ps1");
            CopyNewOperationFile(helperPath, helperCopy);
            string planHash = ReadOperationFileFingerprint(planPath);
            string helperHash = ReadOperationFileFingerprint(helperCopy);
            string planHashPath = Path.Combine(operation, "plan.sha256");
            string helperHashPath = Path.Combine(operation, "helper.sha256");
            WriteNewOperationText(planHashPath, planHash);
            WriteNewOperationText(helperHashPath, helperHash);
            return planPath;
        }

        private static void WriteNewOperationText(string path, string content)
        {
            IO.NoReparseAncestors(path);
            bool occupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(path);
            if (occupied) throw new InvalidOperationException("Offline Apply operation file is already occupied: " + path);
            UTF8Encoding encoding = new UTF8Encoding(false);
            byte[] bytes = encoding.GetBytes(content);
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            bool regularFile = WindowsPackageSetEnvironmentSupport.IsRegularDurableFile(path);
            if (!regularFile) throw new InvalidOperationException("Offline Apply operation file is not an ordinary file: " + path);
        }

        private static void CopyNewOperationFile(string source, string destination)
        {
            IO.NoReparseAncestors(destination);
            bool occupied = WindowsPackageSetEnvironmentSupport.DurablePathOccupied(destination);
            if (occupied) throw new InvalidOperationException("Offline Apply operation file is already occupied: " + destination);
            File.Copy(source, destination, false);
            bool regularFile = WindowsPackageSetEnvironmentSupport.IsRegularDurableFile(destination);
            if (!regularFile) throw new InvalidOperationException("Offline Apply operation file is not an ordinary file: " + destination);
        }

        private static string ReadOperationFileFingerprint(string path)
        {
            WindowsPackageSetEnvironmentSupport.DurableFileSnapshot snapshot = WindowsPackageSetEnvironmentSupport.OpenDurableFileSnapshot(path);
            using (snapshot)
            {
                return snapshot.Fingerprint;
            }
        }

        private static void RequireOrdinaryOperationDirectory(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool directory = (attributes & FileAttributes.Directory) != 0;
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            if (!directory || reparse) throw new InvalidOperationException("Offline Apply operation evidence must use an ordinary directory: " + path);
            IO.NoReparseAncestors(path);
        }

        private static OfflineApplyEntry BuildEntry(string projectRoot, string packageSetRoot, string gitRoot, PackageSetEntryPlan entryPlan)
        {
            PackagePlacement placement = entryPlan.Placement;
            string source = IO.Under(packageSetRoot, placement.SourcePath);
            string original = IO.Under(projectRoot, placement.OriginalProjectPath);
            string projection = IO.Under(projectRoot, placement.ProjectionPath);
            string sourceHead = IO.Git(source, "rev-parse", "HEAD");
            string contentHash = IO.HashTree(source);
            Dictionary<string, string> tracked = IO.GitTree(source);
            SortedDictionary<string, string> sourceFiles = IO.Files(source);
            DevelopmentProjectionState state = entryPlan.Observation.ProjectionState;
            string projectionGitRelative = IO.Relative(gitRoot, projection);
            SortedDictionary<string, string> projectionFiles = new SortedDictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> projectionModes = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> projectionBlobs = new Dictionary<string, string>(StringComparer.Ordinal);
            List<OfflineProjectionExclusion> projectionExclusions = new List<OfflineProjectionExclusion>();
            HashSet<string> excludedPaths = new HashSet<string>(StringComparer.Ordinal);
            List<string> trackedPaths = new List<string>(tracked.Keys);
            trackedPaths.Sort(StringComparer.Ordinal);
            foreach (string trackedPath in trackedPaths)
            {
                bool isMetadata = trackedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
                bool generatedProjectMetadata = trackedPath.EndsWith(".csproj.meta", StringComparison.OrdinalIgnoreCase);
                string generatedCompanionPath = string.Empty;
                if (generatedProjectMetadata) generatedCompanionPath = trackedPath.Substring(0, trackedPath.Length - ".meta".Length);
                bool companionExcluded = generatedProjectMetadata && excludedPaths.Contains(generatedCompanionPath);
                if (companionExcluded)
                {
                    string metadataPlatformRelative = trackedPath.Replace('/', Path.DirectorySeparatorChar);
                    string metadataSourcePath = Path.Combine(source, metadataPlatformRelative);
                    OfflineProjectionExclusion metadataExclusion = new OfflineProjectionExclusion();
                    metadataExclusion.path = trackedPath;
                    metadataExclusion.sourceBlob = tracked[trackedPath].Substring(7);
                    metadataExclusion.sourceHash = IO.HashFile(metadataSourcePath);
                    metadataExclusion.pairedPath = generatedCompanionPath;
                    metadataExclusion.reason = "ProjectIgnoredGeneratedCsprojMetadata";
                    projectionExclusions.Add(metadataExclusion);
                    excludedPaths.Add(trackedPath);
                    continue;
                }
                string projectedPath = PackageRelativePath.Normalize(projectionGitRelative + "/" + trackedPath);
                bool ignored = !isMetadata && WindowsPackageSetEnvironmentSupport.ProjectPathIgnored(gitRoot, projectedPath);
                bool preservedIgnoredFile = ignored && WindowsPackageSetEnvironmentSupport.IndexEntryMatchesSource(gitRoot, projectedPath, tracked[trackedPath]);
                bool requiresExclusion = ignored && !preservedIgnoredFile;
                bool safeExclusion = !requiresExclusion || WindowsPackageSetEnvironmentSupport.IsSafeGeneratedProjectionExclusion(trackedPath);
                if (requiresExclusion && !safeExclusion) throw new InvalidOperationException("Project ignore rules hide an authoritative source file that is not an approved generated .csproj exclusion: " + trackedPath);
                if (requiresExclusion)
                {
                    OfflineProjectionExclusion exclusion = new OfflineProjectionExclusion();
                    exclusion.path = trackedPath;
                    exclusion.sourceBlob = tracked[trackedPath].Substring(7);
                    string platformRelative = trackedPath.Replace('/', Path.DirectorySeparatorChar);
                    string sourcePath = Path.Combine(source, platformRelative);
                    exclusion.sourceHash = IO.HashFile(sourcePath);
                    exclusion.pairedPath = string.Empty;
                    exclusion.reason = "ProjectIgnoredGeneratedCsproj";
                    projectionExclusions.Add(exclusion);
                    excludedPaths.Add(trackedPath);
                    continue;
                }
                string trackedPlatformRelative = trackedPath.Replace('/', Path.DirectorySeparatorChar);
                string sourceFile = Path.Combine(source, trackedPlatformRelative);
                projectionFiles.Add(trackedPath, sourceFile);
                string treeEntry = tracked[trackedPath];
                string projectionMode = treeEntry.Substring(0, 6);
                string projectionBlob = treeEntry.Substring(7);
                projectionModes.Add(trackedPath, projectionMode);
                projectionBlobs.Add(trackedPath, projectionBlob);
            }
            foreach (KeyValuePair<string, string> sourceFile in sourceFiles)
            {
                if (!sourceFile.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                if (projectionFiles.ContainsKey(sourceFile.Key)) continue;
                if (excludedPaths.Contains(sourceFile.Key)) continue;
                bool pairedGeneratedMetadata = WindowsPackageSetEnvironmentSupport.IsPairedGeneratedMetadataExclusion(gitRoot, source, projectionGitRelative, tracked, sourceFile.Key);
                if (pairedGeneratedMetadata)
                {
                    string companionPath = sourceFile.Key.Substring(0, sourceFile.Key.Length - ".meta".Length);
                    bool companionExcluded = excludedPaths.Contains(companionPath);
                    if (!companionExcluded) throw new InvalidOperationException("Generated .csproj metadata is not paired with an approved projection exclusion: " + sourceFile.Key);
                    OfflineProjectionExclusion metadataExclusion = new OfflineProjectionExclusion();
                    metadataExclusion.path = sourceFile.Key;
                    metadataExclusion.sourceBlob = string.Empty;
                    metadataExclusion.sourceHash = IO.HashFile(sourceFile.Value);
                    metadataExclusion.pairedPath = companionPath;
                    metadataExclusion.reason = "ProjectIgnoredGeneratedCsprojMetadata";
                    projectionExclusions.Add(metadataExclusion);
                    excludedPaths.Add(sourceFile.Key);
                    continue;
                }
                bool useCanonicalMetadata = state == DevelopmentProjectionState.Absent || state == DevelopmentProjectionState.CorrectJunction;
                if (!useCanonicalMetadata) continue;
                projectionFiles.Add(sourceFile.Key, sourceFile.Value);
                projectionModes.Add(sourceFile.Key, "100644");
                string projectedPath = PackageRelativePath.Normalize(projectionGitRelative + "/" + sourceFile.Key);
                string projectedBlob = HashProjectedFile(gitRoot, projectedPath, sourceFile.Value);
                projectionBlobs.Add(sourceFile.Key, projectedBlob);
            }

            SortedDictionary<string, OfflineMetadata> metadata = new SortedDictionary<string, OfflineMetadata>(StringComparer.Ordinal);
            bool preserveProjectMetadata = state == DevelopmentProjectionState.PhysicalSnapshot || state == DevelopmentProjectionState.BootstrapGitlink;
            if (preserveProjectMetadata) AddProjectMetadataOverlay(gitRoot, source, original, projectionGitRelative, tracked, excludedPaths, projectionExclusions, projectionFiles, projectionModes, projectionBlobs, metadata);

            List<OfflineFile> files = new List<OfflineFile>();
            foreach (KeyValuePair<string, string> projectionFile in projectionFiles)
            {
                OfflineFile file = new OfflineFile();
                file.path = projectionFile.Key;
                file.mode = projectionModes[projectionFile.Key];
                file.sourceBlob = projectionBlobs[projectionFile.Key];
                files.Add(file);
            }

            OfflineApplyEntry entry = new OfflineApplyEntry();
            entry.identity = placement.Identity;
            entry.sourcePath = source;
            entry.originalPath = original;
            entry.projectionPath = projection;
            entry.sourceRepository = entryPlan.SourceRepository;
            entry.observedRepository = entryPlan.ObservedRepository;
            entry.sourceRevision = sourceHead;
            string exactPin = sourceHead;
            if (entryPlan.Placement.Kind == DevelopmentSourceKind.GitSubmodule) exactPin = entryPlan.Observation.ExactPin;
            entry.exactPin = exactPin;
            entry.contentHash = contentHash;
            entry.originalContentHash = OriginalContentHash(original, state, contentHash);
            entry.originalInventoryHash = OriginalInventoryHash(original, state);
            entry.originalMetaHash = IO.HashFile(original + ".meta");
            string originalGitRelative = IO.Relative(gitRoot, original);
            entry.submoduleSection = WindowsPackageSetEnvironmentSupport.FindSubmoduleSection(gitRoot, originalGitRelative);
            entry.projectionState = entryPlan.Observation.ProjectionState.ToString();
            entry.needsPublish = entryPlan.Assessment.NeedsPublish;
            ValidateOfflineSourceAuthority(entryPlan, entry);
            entry.files = files.ToArray();
            List<OfflineMetadata> metadataRecords = new List<OfflineMetadata>(metadata.Values);
            entry.metadata = metadataRecords.ToArray();
            entry.projectionExclusions = projectionExclusions.ToArray();
            return entry;
        }

        private static void AddProjectMetadataOverlay(string gitRoot, string source, string original, string projectionGitRelative, IReadOnlyDictionary<string, string> tracked, ISet<string> excludedPaths, ICollection<OfflineProjectionExclusion> projectionExclusions, IDictionary<string, string> projectionFiles, IDictionary<string, string> projectionModes, IDictionary<string, string> projectionBlobs, IDictionary<string, OfflineMetadata> metadata)
        {
            string originalProjectRelative = IO.Relative(gitRoot, original);
            bool metadataMatches = WindowsPackageSetEnvironmentSupport.ConversionMetadataMatches(gitRoot, source, original, originalProjectRelative);
            if (!metadataMatches) throw new InvalidOperationException("Project and canonical source metadata cannot be reconciled without changing established GUID ownership.");
            SortedDictionary<string, string> originalFiles = IO.Files(original);
            foreach (KeyValuePair<string, string> originalFile in originalFiles)
            {
                if (!originalFile.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                bool companionExists = WindowsPackageSetEnvironmentSupport.MetadataCompanionExists(source, originalFile.Key);
                if (!companionExists) throw new InvalidOperationException("Project metadata has no authoritative companion in the canonical source: " + originalFile.Key);
                string platformRelative = originalFile.Key.Replace('/', Path.DirectorySeparatorChar);
                string sourceMetadata = Path.Combine(source, platformRelative);
                string companionPath = originalFile.Key.Substring(0, originalFile.Key.Length - ".meta".Length);
                bool generatedProjectMetadata = originalFile.Key.EndsWith(".csproj.meta", StringComparison.OrdinalIgnoreCase);
                bool companionExcluded = excludedPaths.Contains(companionPath);
                bool pairedGeneratedMetadata = generatedProjectMetadata && companionExcluded;
                if (tracked.ContainsKey(originalFile.Key))
                {
                    string sourceMetadataHash = IO.HashFile(sourceMetadata);
                    string projectMetadataHash = IO.HashFile(originalFile.Value);
                    bool metadataDiffers = !sourceMetadataHash.Equals(projectMetadataHash, StringComparison.Ordinal);
                    if (metadataDiffers) throw new InvalidOperationException("Project metadata differs from authoritative tracked source metadata: " + originalFile.Key);
                    continue;
                }
                RequireIgnoredSourceMetadata(source, originalFile.Key);
                string projectHash = IO.HashFile(originalFile.Value);
                string sourcePreviousHash = IO.HashFile(sourceMetadata);
                bool sourceAbsent = sourcePreviousHash.Equals("missing", StringComparison.Ordinal);
                bool sourceIdentical = sourcePreviousHash.Equals(projectHash, StringComparison.Ordinal);
                if (!sourceAbsent && !sourceIdentical) throw new InvalidOperationException("Project metadata conflicts with existing canonical source metadata: " + originalFile.Key);
                OfflineMetadata record = new OfflineMetadata();
                record.path = originalFile.Key;
                record.projectHash = projectHash;
                record.sourcePreviousHash = sourcePreviousHash;
                byte[] projectMetadataBytes = File.ReadAllBytes(originalFile.Value);
                record.contentBase64 = Convert.ToBase64String(projectMetadataBytes);
                metadata[originalFile.Key] = record;
                if (pairedGeneratedMetadata)
                {
                    if (!excludedPaths.Contains(originalFile.Key))
                    {
                        OfflineProjectionExclusion exclusion = new OfflineProjectionExclusion();
                        exclusion.path = originalFile.Key;
                        exclusion.sourceBlob = string.Empty;
                        exclusion.sourceHash = projectHash;
                        exclusion.pairedPath = companionPath;
                        exclusion.reason = "ProjectIgnoredGeneratedCsprojMetadata";
                        projectionExclusions.Add(exclusion);
                        excludedPaths.Add(originalFile.Key);
                    }
                    continue;
                }
                if (excludedPaths.Contains(originalFile.Key)) continue;
                projectionFiles[originalFile.Key] = originalFile.Value;
                projectionModes[originalFile.Key] = "100644";
                string projectedPath = PackageRelativePath.Normalize(projectionGitRelative + "/" + originalFile.Key);
                projectionBlobs[originalFile.Key] = HashProjectedFile(gitRoot, projectedPath, originalFile.Value);
            }
        }

        private static void RequireIgnoredSourceMetadata(string source, string relative)
        {
            bool ignored = WindowsPackageSetEnvironmentSupport.SourcePathIgnored(source, relative);
            if (!ignored) throw new InvalidOperationException("Project metadata cannot be safely materialized because the canonical source does not ignore it: " + relative);
        }

        private static string OriginalContentHash(string original, DevelopmentProjectionState state, string sourceContentHash)
        {
            if (state == DevelopmentProjectionState.Absent) return "missing";
            if (state == DevelopmentProjectionState.CorrectJunction) return sourceContentHash;
            bool originalExists = Directory.Exists(original);
            if (!originalExists) throw new InvalidOperationException("Observed Project source is unavailable while exporting its immutable content evidence.");
            return IO.HashTree(original);
        }

        private static string OriginalInventoryHash(string original, DevelopmentProjectionState state)
        {
            if (state == DevelopmentProjectionState.Absent) return "missing";
            bool originalExists = Directory.Exists(original);
            if (!originalExists) throw new InvalidOperationException("Observed Project source is unavailable while exporting its immutable inventory evidence.");
            return WindowsPackageSetEnvironmentSupport.FileInventoryFingerprint(original);
        }

        private static string HashProjectedFile(string gitRoot, string projectedPath, string sourceFile)
        {
            return IO.Git(gitRoot, "hash-object", "--path=" + projectedPath, "--", sourceFile);
        }

        private static void ValidateOfflineSourceAuthority(PackageSetEntryPlan entryPlan, OfflineApplyEntry entry)
        {
            bool gitSubmodule = entryPlan.Placement.Kind == DevelopmentSourceKind.GitSubmodule;
            if (gitSubmodule)
            {
                bool sourceRepositoryMissing = string.IsNullOrWhiteSpace(entry.sourceRepository);
                bool observedRepositoryMissing = string.IsNullOrWhiteSpace(entry.observedRepository);
                if (sourceRepositoryMissing || observedRepositoryMissing)
                    throw new InvalidOperationException(entry.identity + ": portable source repository authority is unavailable.");
                if (!entry.sourceRepository.Equals(entry.observedRepository, StringComparison.Ordinal))
                    throw new InvalidOperationException(entry.identity + ": observed source repository differs from PackageSet authority.");
                if (!entry.sourceRevision.Equals(entry.exactPin, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(entry.identity + ": prepared source HEAD differs from its exact PackageSet pin.");
                if (entry.needsPublish)
                    throw new InvalidOperationException(entry.identity + ": a gitlink-backed source cannot be marked as a LocalDraft publication.");
                return;
            }

            bool sourceRepositoryPresent = !string.IsNullOrWhiteSpace(entry.sourceRepository);
            bool observedRepositoryPresent = !string.IsNullOrWhiteSpace(entry.observedRepository);
            if (sourceRepositoryPresent || observedRepositoryPresent)
                throw new InvalidOperationException(entry.identity + ": LocalDraft repository fields must remain empty until publication.");
            if (!entry.needsPublish)
                throw new InvalidOperationException(entry.identity + ": LocalDraft must remain explicitly marked NeedsPublish.");
            if (!entry.sourceRevision.Equals(entry.exactPin, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(entry.identity + ": LocalDraft exact pin must be its prepared canonical source HEAD.");
        }

        private static void AddWrite(string gitRoot, ICollection<OfflineWrite> writes, string relative, object value, string observedFingerprint)
        {
            string absolute = IO.Under(gitRoot, relative);
            string currentFingerprint = IO.HashFile(absolute);
            bool fingerprintChanged = !currentFingerprint.Equals(observedFingerprint, StringComparison.Ordinal);
            if (fingerprintChanged) throw new InvalidOperationException("Durable binding changed after observation: " + relative);
            OfflineWrite write = new OfflineWrite();
            write.path = absolute;
            write.previousHash = observedFingerprint;
            string json = JsonUtility.ToJson(value, true);
            write.content = json + "\n";
            writes.Add(write);
        }

        private static void AddRuntimeBindings(string projectRoot, string packageSetRoot, string gitRoot, ICollection<OfflineWrite> writes, IReadOnlyList<PackagePlacement> runtime, string observedFingerprint)
        {
            List<RuntimeBindingDocument> components = new List<RuntimeBindingDocument>();
            foreach (PackagePlacement placement in runtime)
            {
                string source = IO.Under(packageSetRoot, placement.SourcePath);
                RuntimeBindingDocument component = new RuntimeBindingDocument();
                component.id = placement.Identity;
                string repository = WindowsPackageSetEnvironmentSupport.CheckoutRemote(source);
                component.repository = repository;
                string projectionPath = IO.Under(projectRoot, placement.ProjectionPath);
                component.path = IO.Relative(gitRoot, projectionPath);
                components.Add(component);
            }
            RuntimeBindingsDocument document = new RuntimeBindingsDocument();
            document.schemaVersion = 1;
            document.components = components.ToArray();
            AddWrite(gitRoot, writes, "PackageManagement/runtime.bindings.json", document, observedFingerprint);
        }
    }
}
