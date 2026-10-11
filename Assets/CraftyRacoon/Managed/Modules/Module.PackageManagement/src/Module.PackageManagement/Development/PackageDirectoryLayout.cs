using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CraftyRacoon.PackageManagement.Development
{
    public enum DevelopmentSourceKind { GitSubmodule, LocalDraft }

    /// <summary>Portable location rules only. Filesystem/reparse-point checks belong to the host.</summary>
    public static class PackageRelativePath
    {
        public static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Package path is empty.", nameof(value));
            string path = value.Replace('\\', '/');
            if (path.StartsWith("/", StringComparison.Ordinal)) throw new ArgumentException("Package paths must be relative.");
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal) ||
                    segment.Any(c => char.IsControl(c) || ":*?\"<>|".IndexOf(c) >= 0))
                    throw new ArgumentException("Unsafe package path: " + value);
                string stem = segment.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '1' && stem[3] <= '9'))
                    throw new ArgumentException("Reserved filesystem name: " + segment);
                if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Git metadata is not a package location.");
            }
            return path;
        }

        public static bool IsWithin(string parent, string child) =>
            child.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);

        public static bool Overlaps(string first, string second) =>
            first.Equals(second, StringComparison.OrdinalIgnoreCase) || IsWithin(first, second) || IsWithin(second, first);
    }

    public sealed class PackagePlacement
    {
        public PackagePlacement(string identity, string sourcePath, string originalProjectPath,
            string projectionPath, DevelopmentSourceKind kind, bool bootstrapRuntime = false)
        {
            Identity = PackageRelativePath.Normalize(identity);
            if (Identity.Contains("/")) throw new ArgumentException("Identity must be a single canonical name.");
            SourcePath = PackageRelativePath.Normalize(sourcePath);
            OriginalProjectPath = PackageRelativePath.Normalize(originalProjectPath);
            ProjectionPath = PackageRelativePath.Normalize(projectionPath);
            if (!Enum.IsDefined(typeof(DevelopmentSourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind;
            BootstrapRuntime = bootstrapRuntime;
        }

        public string Identity { get; }
        public string SourcePath { get; }
        public string OriginalProjectPath { get; }
        public string ProjectionPath { get; }
        public DevelopmentSourceKind Kind { get; }
        public bool BootstrapRuntime { get; }
    }

    public sealed class PackageDirectoryLayout
    {
        public PackageDirectoryLayout(string sourceRoot, string projectionRoot, IEnumerable<PackagePlacement> placements)
        {
            SourceRoot = PackageRelativePath.Normalize(sourceRoot);
            ProjectionRoot = PackageRelativePath.Normalize(projectionRoot);
            var entries = (placements ?? throw new ArgumentNullException(nameof(placements))).ToArray();
            if (entries.Any(p => p == null)) throw new ArgumentException("Placement cannot be null.");
            for (int i = 0; i < entries.Length; i++)
            {
                PackagePlacement p = entries[i];
                if (!PackageRelativePath.IsWithin(SourceRoot, p.SourcePath)) throw new ArgumentException(p.Identity + ": source escapes source root.");
                if (!PackageRelativePath.IsWithin(ProjectionRoot, p.ProjectionPath)) throw new ArgumentException(p.Identity + ": projection escapes managed root.");
                // Parent -> child moves would move the destination into its own backup.
                if (!p.OriginalProjectPath.Equals(p.ProjectionPath, StringComparison.OrdinalIgnoreCase) &&
                    PackageRelativePath.Overlaps(p.OriginalProjectPath, p.ProjectionPath))
                    throw new ArgumentException(p.Identity + ": overlapping relocation is not supported.");
                for (int j = 0; j < i; j++)
                {
                    PackagePlacement other = entries[j];
                    if (p.Identity.Equals(other.Identity, StringComparison.OrdinalIgnoreCase) ||
                        PackageRelativePath.Overlaps(p.SourcePath, other.SourcePath) ||
                        PackageRelativePath.Overlaps(p.ProjectionPath, other.ProjectionPath) ||
                        PackageRelativePath.Overlaps(p.OriginalProjectPath, other.OriginalProjectPath) ||
                        PackageRelativePath.Overlaps(p.OriginalProjectPath, other.ProjectionPath) ||
                        PackageRelativePath.Overlaps(p.ProjectionPath, other.OriginalProjectPath))
                        throw new ArgumentException("Overlapping package ownership: " + p.Identity + " / " + other.Identity);
                }
            }
            Placements = new ReadOnlyCollection<PackagePlacement>(entries);
        }

        public string SourceRoot { get; }
        public string ProjectionRoot { get; }
        public IReadOnlyList<PackagePlacement> Placements { get; }
    }
}
