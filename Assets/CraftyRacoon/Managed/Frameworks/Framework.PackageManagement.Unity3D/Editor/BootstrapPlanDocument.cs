using System;
using UnityEngine;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor
{
    [Serializable]
    internal sealed class BootstrapPlanDocument
    {
        public int schemaVersion;
        public string bootstrapper = string.Empty;
        public string catalogVersion = string.Empty;
        public string planId = string.Empty;
        public string initialSourceRoot = string.Empty;
        public BootstrapPlanComponent[] components = Array.Empty<BootstrapPlanComponent>();
    }

    [Serializable]
    internal sealed class BootstrapPlanComponent
    {
        public string id = string.Empty;
        public string repository = string.Empty;
        public string revision = string.Empty;
        public string path = string.Empty;
    }
}
