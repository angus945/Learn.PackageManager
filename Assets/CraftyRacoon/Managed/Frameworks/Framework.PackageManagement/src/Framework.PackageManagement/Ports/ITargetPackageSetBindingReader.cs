using CraftyRacoon.PackageManagement.PackageSets;

namespace CraftyRacoon.PackageManagement.Framework.Ports
{
    public interface ITargetPackageSetBindingReader
    {
        PackageSetBinding ReadBinding(string targetRoot);
    }
}
