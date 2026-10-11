using System;

namespace CraftyRacoon.PackageManagement.PackageSets
{
    public static class PackageSetDiscoveryEvaluator
    {
        public static PackageSetDiscoverySnapshot Evaluate(
            PackageSetBinding binding,
            bool checkoutExists,
            bool manifestValid,
            bool requiredSourcesInitialized,
            bool exactRevisionAligned)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));

            if (!binding.IsConfigured)
                return PackageSetDiscoverySnapshot.NotConfigured();

            if (!checkoutExists)
            {
                return new PackageSetDiscoverySnapshot(
                    binding,
                    PackageSetStatus.Missing,
                    "The configured PackageSet checkout does not exist.",
                    false,
                    false,
                    false);
            }

            if (!manifestValid)
            {
                return new PackageSetDiscoverySnapshot(
                    binding,
                    PackageSetStatus.Invalid,
                    "The PackageSet manifest is invalid.",
                    false,
                    requiredSourcesInitialized,
                    exactRevisionAligned);
            }

            if (!requiredSourcesInitialized || !exactRevisionAligned)
            {
                return new PackageSetDiscoverySnapshot(
                    binding,
                    PackageSetStatus.Available,
                    !requiredSourcesInitialized
                        ? "PackageSet exists but required sources are not initialized."
                        : "PackageSet exists but source revisions do not match the declared exact pins.",
                    true,
                    requiredSourcesInitialized,
                    exactRevisionAligned);
            }

            return new PackageSetDiscoverySnapshot(
                binding,
                PackageSetStatus.Ready,
                "PackageSet is ready.",
                true,
                true,
                true);
        }
    }
}
