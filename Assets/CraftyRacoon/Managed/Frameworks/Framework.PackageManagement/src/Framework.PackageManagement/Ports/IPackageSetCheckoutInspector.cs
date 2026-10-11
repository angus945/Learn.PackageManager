namespace CraftyRacoon.PackageManagement.Framework.Ports
{
    public interface IPackageSetCheckoutInspector
    {
        bool Exists(string localCheckoutPath);
    }
}
