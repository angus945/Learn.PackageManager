using System;
using CraftyRacoon.PackageManagement.Framework.Ports;
using CraftyRacoon.PackageManagement.PackageSets;

namespace CraftyRacoon.PackageManagement.Framework.Discovery
{
    public sealed class PackageSetDiscoveryWorkflow
    {
        private readonly ITargetRootValidator targetRootValidator;
        private readonly ITargetPackageSetBindingReader bindingReader;
        private readonly IPackageSetCheckoutInspector checkoutInspector;
        private readonly IPackageSetManifestInspector manifestInspector;

        public PackageSetDiscoveryWorkflow(
            ITargetRootValidator targetRootValidator,
            ITargetPackageSetBindingReader bindingReader,
            IPackageSetCheckoutInspector checkoutInspector,
            IPackageSetManifestInspector manifestInspector)
        {
            this.targetRootValidator = targetRootValidator ?? throw new ArgumentNullException(nameof(targetRootValidator));
            this.bindingReader = bindingReader ?? throw new ArgumentNullException(nameof(bindingReader));
            this.checkoutInspector = checkoutInspector ?? throw new ArgumentNullException(nameof(checkoutInspector));
            this.manifestInspector = manifestInspector ?? throw new ArgumentNullException(nameof(manifestInspector));
        }

        public PackageSetDiscoveryResult Execute(PackageSetDiscoveryRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            TargetRootValidation targetValidation = targetRootValidator.Validate(request.TargetRoot);
            if (!targetValidation.IsValid)
            {
                PackageSetDiscoverySnapshot invalidTargetSnapshot =
                    new PackageSetDiscoverySnapshot(
                        new PackageSetBinding(string.Empty, string.Empty),
                        PackageSetStatus.Invalid,
                        "Target root is invalid.",
                        false,
                        false,
                        false);

                return new PackageSetDiscoveryResult(
                    request.TargetRoot,
                    invalidTargetSnapshot,
                    targetValidation.Detail);
            }

            PackageSetBinding binding = bindingReader.ReadBinding(request.TargetRoot);
            if (!binding.IsConfigured)
            {
                return new PackageSetDiscoveryResult(
                    request.TargetRoot,
                    PackageSetDiscoverySnapshot.NotConfigured(),
                    string.Empty);
            }

            bool checkoutExists = checkoutInspector.Exists(binding.LocalCheckoutPath);
            if (!checkoutExists)
            {
                return new PackageSetDiscoveryResult(
                    request.TargetRoot,
                    PackageSetDiscoveryEvaluator.Evaluate(
                        binding,
                        false,
                        false,
                        false,
                        false),
                    string.Empty);
            }

            PackageSetManifestInspection manifest =
                manifestInspector.Inspect(binding.LocalCheckoutPath);

            PackageSetDiscoverySnapshot snapshot =
                PackageSetDiscoveryEvaluator.Evaluate(
                    binding,
                    true,
                    manifest.IsValid,
                    manifest.RequiredSourcesInitialized,
                    manifest.ExactRevisionAligned);

            return new PackageSetDiscoveryResult(
                request.TargetRoot,
                snapshot,
                manifest.Detail);
        }
    }
}
