using System;
using System.Collections.Generic;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor
{
    internal sealed class PackageManagementCompositionStatus
    {
        private PackageManagementCompositionStatus(bool succeeded, string summary, PackageManagementSourceComposition? composition, IReadOnlyList<string> diagnostics)
        {
            Succeeded = succeeded;
            Summary = summary;
            Composition = composition;
            Diagnostics = diagnostics;
        }

        internal bool Succeeded { get; }
        internal string Summary { get; }
        internal PackageManagementSourceComposition? Composition { get; }
        internal IReadOnlyList<string> Diagnostics { get; }

        internal static PackageManagementCompositionStatus Observe()
        {
            try
            {
                PackageManagementSourceComposition composition = PackageManagementSourceComposition.Discover();
                List<string> diagnostics = new List<string>();

                AddBindingDiagnostics(composition.Module, diagnostics);
                AddBindingDiagnostics(composition.Framework, diagnostics);
                AddBindingDiagnostics(composition.UnityFramework, diagnostics);

                bool succeeded = composition.SourceCompositionReady && composition.TypedFrameworkAvailable;
                string summary = BuildSummary(composition, succeeded);
                return new PackageManagementCompositionStatus(succeeded, summary, composition, diagnostics);
            }
            catch (Exception exception)
            {
                string[] diagnostics = new string[] { exception.Message };
                return new PackageManagementCompositionStatus(false, "Package Management Unity runtime composition unavailable.", null, diagnostics);
            }
        }

        private static string BuildSummary(PackageManagementSourceComposition composition, bool succeeded)
        {
            if (!succeeded) return "Package Management Unity runtime composition incomplete.";
            if (composition.IsSelfObserved) return "Package Management runtime self-observed; ready for initialization.";
            return "Package Management Unity runtime composition ready.";
        }

        private static void AddBindingDiagnostics(PackageManagementSourceComposition.SourceBinding binding, ICollection<string> diagnostics)
        {
            foreach (string diagnostic in binding.Diagnostics)
                diagnostics.Add(binding.Id + ": " + diagnostic);
        }
    }
}
