using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Framework.Snapshot;

namespace CraftyRacoon.PackageManagement.Framework.Ports
{
    public interface IPackageSetSnapshotInventoryReader
    {
        IReadOnlyList<ManagedSnapshotSubject> Read(
            string targetRoot,
            string packageSetRoot);
    }
}
