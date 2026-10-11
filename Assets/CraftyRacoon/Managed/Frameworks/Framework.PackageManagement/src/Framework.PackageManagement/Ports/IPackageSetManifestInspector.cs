namespace CraftyRacoon.PackageManagement.Framework.Ports
{
    public interface IPackageSetManifestInspector
    {
        PackageSetManifestInspection Inspect(string localCheckoutPath);
    }

    public sealed class PackageSetManifestInspection
    {
        public PackageSetManifestInspection(
            bool isValid,
            bool requiredSourcesInitialized,
            bool exactRevisionAligned,
            string detail)
        {
            IsValid = isValid;
            RequiredSourcesInitialized = requiredSourcesInitialized;
            ExactRevisionAligned = exactRevisionAligned;
            Detail = detail ?? string.Empty;
        }

        public bool IsValid { get; }
        public bool RequiredSourcesInitialized { get; }
        public bool ExactRevisionAligned { get; }
        public string Detail { get; }
    }
}
