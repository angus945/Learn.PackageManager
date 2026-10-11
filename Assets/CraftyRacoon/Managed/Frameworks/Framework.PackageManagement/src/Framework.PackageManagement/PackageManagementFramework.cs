using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Framework.Discovery;
using CraftyRacoon.PackageManagement.Framework.Snapshot;
using CraftyRacoon.PackageManagement.PackageSets;

namespace CraftyRacoon.PackageManagement.Framework
{
    public sealed class PackageManagementFramework
    {
        private readonly PackageSetDiscoveryWorkflow packageSetDiscovery;
        private readonly SnapshotObservationWorkflow? snapshotObservation;

        public PackageManagementFramework(
            PackageSetDiscoveryWorkflow packageSetDiscovery)
            : this(packageSetDiscovery, null)
        {
        }

        public PackageManagementFramework(
            PackageSetDiscoveryWorkflow packageSetDiscovery,
            SnapshotObservationWorkflow? snapshotObservation)
        {
            this.packageSetDiscovery =
                packageSetDiscovery ?? throw new ArgumentNullException(nameof(packageSetDiscovery));
            this.snapshotObservation = snapshotObservation;
        }

        public PackageSetDiscoveryResult DiscoverPackageSet(string targetRoot)
        {
            return packageSetDiscovery.Execute(new PackageSetDiscoveryRequest(targetRoot));
        }

        public IReadOnlyList<SnapshotObservationResult> ObserveSnapshots(
            PackageSetDiscoveryResult discovery)
        {
            if (discovery == null)
                throw new ArgumentNullException(nameof(discovery));
            if (snapshotObservation == null)
                throw new InvalidOperationException("SnapshotObservationWorkflowNotConfigured");

            PackageSetStatus status = discovery.Snapshot.Status;
            if (status == PackageSetStatus.NotConfigured ||
                status == PackageSetStatus.Missing ||
                status == PackageSetStatus.Invalid)
            {
                throw new InvalidOperationException(
                    "Snapshot observation requires an available PackageSet checkout.");
            }

            string packageSetRoot = discovery.Snapshot.Binding.LocalCheckoutPath;
            if (string.IsNullOrWhiteSpace(packageSetRoot))
                throw new InvalidOperationException("PackageSetLocalCheckoutPathMissing");

            return snapshotObservation.Execute(
                new SnapshotObservationRequest(
                    discovery.TargetRoot,
                    packageSetRoot));
        }
    }
}
