using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Development;

namespace CraftyRacoon.PackageManagement.Framework.Development
{
    /// <summary>
    /// Authoritative canonical-source directory policy for PackageSet development composition.
    /// The configurable root contains exactly three architectural source buckets.
    /// Host specializations must consume this policy instead of reconstructing paths.
    /// </summary>
    public static class PackageSourceDirectoryPolicy
    {
        public const string FrameworksDirectory = "Frameworks";
        public const string ModulesDirectory = "Modules";
        public const string WorkspacesDirectory = "Workspaces";

        public static string ResolveCanonicalSourcePath(string sourceRoot, string identity)
        {
            string root = PackageRelativePath.Normalize(sourceRoot);
            string canonicalIdentity = NormalizeIdentity(identity);
            return root + "/" + ResolveBucket(canonicalIdentity) + "/" + canonicalIdentity;
        }

        public static string ResolveManagedProjectionPath(string projectionRoot, string identity)
        {
            string root = PackageRelativePath.Normalize(projectionRoot);
            string canonicalIdentity = NormalizeIdentity(identity);
            return root + "/" + ResolveBucket(canonicalIdentity) + "/" + canonicalIdentity;
        }

        public static PackagePlacement Canonicalize(string sourceRoot, string projectionRoot, PackagePlacement placement)
        {
            if (placement == null) throw new ArgumentNullException(nameof(placement));
            string sourcePath = ResolveCanonicalSourcePath(sourceRoot, placement.Identity);
            string projectionPath = ResolveManagedProjectionPath(projectionRoot, placement.Identity);
            return new PackagePlacement(placement.Identity, sourcePath, placement.OriginalProjectPath, projectionPath, placement.Kind, placement.BootstrapRuntime);
        }

        public static IReadOnlyList<PackagePlacement> Canonicalize(string sourceRoot, string projectionRoot, IEnumerable<PackagePlacement> placements)
        {
            if (placements == null) throw new ArgumentNullException(nameof(placements));
            List<PackagePlacement> canonical = new List<PackagePlacement>();
            foreach (PackagePlacement placement in placements)
            {
                PackagePlacement canonicalPlacement = Canonicalize(sourceRoot, projectionRoot, placement);
                canonical.Add(canonicalPlacement);
            }
            return canonical.AsReadOnly();
        }

        public static void RequireCanonicalLayout(PackageDirectoryLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            foreach (PackagePlacement placement in layout.Placements)
            {
                string expectedSource = ResolveCanonicalSourcePath(layout.SourceRoot, placement.Identity);
                if (!placement.SourcePath.Equals(expectedSource, StringComparison.OrdinalIgnoreCase))
                {
                    string message = placement.Identity + ": non-canonical PackageSet source location '" + placement.SourcePath + "'. Expected '" + expectedSource + "'. Use a PackageSet with a canonical source layout.";
                    throw new InvalidOperationException(message);
                }

                string expectedProjection = ResolveManagedProjectionPath(layout.ProjectionRoot, placement.Identity);
                if (!placement.ProjectionPath.Equals(expectedProjection, StringComparison.OrdinalIgnoreCase))
                {
                    string message = placement.Identity + ": non-canonical Project projection location '" + placement.ProjectionPath + "'. Expected '" + expectedProjection + "'. Use the declared canonical projection layout.";
                    throw new InvalidOperationException(message);
                }
            }
        }

        private static string NormalizeIdentity(string identity)
        {
            string canonical = PackageRelativePath.Normalize(identity);
            if (canonical.Contains("/")) throw new ArgumentException("Package identity must be a single canonical name.", nameof(identity));
            return canonical;
        }

        private static string ResolveBucket(string identity)
        {
            if (identity.StartsWith("Framework.", StringComparison.Ordinal)) return FrameworksDirectory;
            if (identity.StartsWith("Module.", StringComparison.Ordinal)) return ModulesDirectory;
            if (identity.StartsWith("Workspace.", StringComparison.Ordinal)) return WorkspacesDirectory;

            string message = "Unsupported PackageSet source identity '" + identity + "'. Managed reusable sources must be Framework.*, Module.*, or Workspace.*.";
            throw new InvalidOperationException(message);
        }
    }
}
