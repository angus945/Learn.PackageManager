using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Development;

namespace CraftyRacoon.PackageManagement.Framework.Development
{
    internal static class PackageSetPlanValidation
    {
        private static readonly string[] RuntimeIdentities =
        {
            "Framework.PackageManagement",
            "Framework.PackageManagement.Unity3D",
            "Module.PackageManagement"
        };

        internal static List<CreatePackageSetSelection> SortSelections(IReadOnlyList<CreatePackageSetSelection> selections)
        {
            List<CreatePackageSetSelection> sorted = new List<CreatePackageSetSelection>();
            foreach (CreatePackageSetSelection selection in selections) sorted.Add(selection);
            sorted.Sort(CreateSelectionIdentityComparer.Instance);
            return sorted;
        }

        internal static List<AuthoritativePackageSetEntry> SortAuthoritativeEntries(IReadOnlyList<AuthoritativePackageSetEntry> entries)
        {
            List<AuthoritativePackageSetEntry> sorted = new List<AuthoritativePackageSetEntry>();
            foreach (AuthoritativePackageSetEntry entry in entries) sorted.Add(entry);
            sorted.Sort(AuthoritativeEntryIdentityComparer.Instance);
            return sorted;
        }

        internal static void CollectPlacementConflicts(string sourceRoot, string projectionRoot, IReadOnlyList<PackagePlacement> placements, List<string> conflicts)
        {
            if (placements.Count == 0) conflicts.Add("At least one PackageSet source is required.");
            for (int index = 0; index < placements.Count; index++)
            {
                PackagePlacement placement = placements[index];
                CollectCanonicalPathConflicts(sourceRoot, projectionRoot, placement, conflicts);
                CollectOriginalPathConflicts(placement, conflicts);
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    PackagePlacement previous = placements[previousIndex];
                    CollectPairConflicts(previous, placement, conflicts);
                }
            }
        }

        internal static void CollectRuntimeConflicts(IReadOnlyList<PackagePlacement> placements, List<string> conflicts)
        {
            List<string> runtimeIdentities = new List<string>();
            foreach (PackagePlacement placement in placements)
            {
                bool runtimeIdentity = IsRuntimeIdentity(placement.Identity);
                if (placement.BootstrapRuntime != runtimeIdentity) conflicts.Add(placement.Identity + ": Package Manager runtime classification does not match its canonical identity.");
                if (runtimeIdentity) runtimeIdentities.Add(placement.Identity);
            }
            runtimeIdentities.Sort(StringComparer.Ordinal);
            if (runtimeIdentities.Count == 0) return;
            if (runtimeIdentities.Count != RuntimeIdentities.Length)
            {
                conflicts.Add("Package Manager runtime composition must contain either zero entries or all three runtime entries.");
                return;
            }
            for (int index = 0; index < RuntimeIdentities.Length; index++)
            {
                if (runtimeIdentities[index].Equals(RuntimeIdentities[index], StringComparison.Ordinal)) continue;
                conflicts.Add("Package Manager runtime composition contains an unexpected or incomplete identity set.");
                return;
            }
        }

        private static bool IsRuntimeIdentity(string identity)
        {
            foreach (string runtimeIdentity in RuntimeIdentities)
            {
                if (identity.Equals(runtimeIdentity, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        internal static void AddValidationConflicts(IReadOnlyList<string> validationConflicts, List<string> conflicts)
        {
            if (validationConflicts == null)
            {
                conflicts.Add("The Host validation result was unavailable.");
                return;
            }
            foreach (string validationConflict in validationConflicts)
            {
                if (!string.IsNullOrWhiteSpace(validationConflict)) conflicts.Add(validationConflict);
            }
        }

        internal static void ThrowIfAny(List<string> conflicts)
        {
            if (conflicts.Count != 0) throw new PackageSetWorkflowConflictException(conflicts);
        }

        internal static void RequireUnchangedProject(ProjectWorkflowObservation before, ProjectWorkflowObservation after)
        {
            if (after == null) throw new InvalidOperationException("The Host did not return the post-Prepare Project observation.");
            bool sameHead = before.ProjectHead.Equals(after.ProjectHead, StringComparison.OrdinalIgnoreCase);
            bool sameIndex = before.IndexFingerprint.Equals(after.IndexFingerprint, StringComparison.OrdinalIgnoreCase);
            bool sameEvidence = before.EvidenceToken.Equals(after.EvidenceToken, StringComparison.Ordinal);
            if (!sameHead || !sameIndex || !sameEvidence) throw new InvalidOperationException("Project state changed during preparation. Preserve the operation evidence and do not run offline Apply.");
        }

        private static void CollectCanonicalPathConflicts(string sourceRoot, string projectionRoot, PackagePlacement placement, List<string> conflicts)
        {
            if (!PackageRelativePath.IsWithin(sourceRoot, placement.SourcePath)) conflicts.Add(placement.Identity + ": canonical source escapes the PackageSet source root.");
            if (!PackageRelativePath.IsWithin(projectionRoot, placement.ProjectionPath)) conflicts.Add(placement.Identity + ": managed projection escapes the Project projection root.");
            try
            {
                string expectedSource = PackageSourceDirectoryPolicy.ResolveCanonicalSourcePath(sourceRoot, placement.Identity);
                string expectedProjection = PackageSourceDirectoryPolicy.ResolveManagedProjectionPath(projectionRoot, placement.Identity);
                if (!placement.SourcePath.Equals(expectedSource, StringComparison.OrdinalIgnoreCase)) conflicts.Add(placement.Identity + ": PackageSet source path is not canonical. Expected " + expectedSource + ".");
                if (!placement.ProjectionPath.Equals(expectedProjection, StringComparison.OrdinalIgnoreCase)) conflicts.Add(placement.Identity + ": Project projection path is not canonical. Expected " + expectedProjection + ".");
            }
            catch (InvalidOperationException error)
            {
                conflicts.Add(placement.Identity + ": " + error.Message);
            }
        }

        private static void CollectOriginalPathConflicts(PackagePlacement placement, List<string> conflicts)
        {
            bool samePath = placement.OriginalProjectPath.Equals(placement.ProjectionPath, StringComparison.OrdinalIgnoreCase);
            bool overlapsProjection = PackageRelativePath.Overlaps(placement.OriginalProjectPath, placement.ProjectionPath);
            if (!samePath && overlapsProjection) conflicts.Add(placement.Identity + ": Project source and managed projection paths overlap.");
        }

        private static void CollectPairConflicts(PackagePlacement first, PackagePlacement second, List<string> conflicts)
        {
            if (first.Identity.Equals(second.Identity, StringComparison.OrdinalIgnoreCase)) conflicts.Add("Duplicate PackageSet identity: " + first.Identity + ".");
            if (PackageRelativePath.Overlaps(first.SourcePath, second.SourcePath)) conflicts.Add(first.Identity + " / " + second.Identity + ": canonical source ownership overlaps.");
            if (PackageRelativePath.Overlaps(first.ProjectionPath, second.ProjectionPath)) conflicts.Add(first.Identity + " / " + second.Identity + ": managed projection ownership overlaps.");
            if (PackageRelativePath.Overlaps(first.OriginalProjectPath, second.OriginalProjectPath)) conflicts.Add(first.Identity + " / " + second.Identity + ": Project source ownership overlaps.");
            if (PackageRelativePath.Overlaps(first.OriginalProjectPath, second.ProjectionPath)) conflicts.Add(first.Identity + " / " + second.Identity + ": first Project source overlaps the second managed projection.");
            if (PackageRelativePath.Overlaps(first.ProjectionPath, second.OriginalProjectPath)) conflicts.Add(first.Identity + " / " + second.Identity + ": first managed projection overlaps the second Project source.");
        }

        private sealed class CreateSelectionIdentityComparer : IComparer<CreatePackageSetSelection>
        {
            internal static readonly CreateSelectionIdentityComparer Instance = new CreateSelectionIdentityComparer();

            public int Compare(CreatePackageSetSelection first, CreatePackageSetSelection second)
            {
                return StringComparer.Ordinal.Compare(first.Identity, second.Identity);
            }
        }

        private sealed class AuthoritativeEntryIdentityComparer : IComparer<AuthoritativePackageSetEntry>
        {
            internal static readonly AuthoritativeEntryIdentityComparer Instance = new AuthoritativeEntryIdentityComparer();

            public int Compare(AuthoritativePackageSetEntry first, AuthoritativePackageSetEntry second)
            {
                return StringComparer.Ordinal.Compare(first.Identity, second.Identity);
            }
        }
    }
}
