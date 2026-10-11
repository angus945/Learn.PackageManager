using System;

namespace CraftyRacoon.PackageManagement.Framework.Discovery
{
    public sealed class PackageSetDiscoveryRequest
    {
        public PackageSetDiscoveryRequest(string targetRoot)
        {
            if (string.IsNullOrWhiteSpace(targetRoot))
                throw new ArgumentException("Target root is required.", nameof(targetRoot));

            TargetRoot = targetRoot;
        }

        public string TargetRoot { get; }
    }
}
