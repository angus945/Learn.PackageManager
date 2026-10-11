using System;

namespace CraftyRacoon.PackageManagement.PackageSets
{
    public sealed class PackageSetBinding
    {
        public PackageSetBinding(string repositoryIdentity, string localCheckoutPath)
        {
            RepositoryIdentity = repositoryIdentity ?? string.Empty;
            LocalCheckoutPath = localCheckoutPath ?? string.Empty;
        }

        public string RepositoryIdentity { get; }
        public string LocalCheckoutPath { get; }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(RepositoryIdentity) ||
            !string.IsNullOrWhiteSpace(LocalCheckoutPath);
    }
}
