using System;

namespace CraftyRacoon.PackageManagement.Unity3D.Editor.Development
{
    [Serializable] internal sealed class PackageSetDocument
    {
        public int schemaVersion;
        public string identity = string.Empty;
        public PackageSetTargetDocument target = null!;
        public string sourceRoot = string.Empty;
        public string managedProjectionRoot = string.Empty;
        public PackageSetEntryDocument[] entries = null!;
    }
    [Serializable] internal sealed class PackageSetTargetDocument
    { public string identity = string.Empty; public string repository = string.Empty; }
    [Serializable] internal sealed class PackageSetEntryDocument
    {
        public string identity = string.Empty;
        public string sourcePath = string.Empty;
        public string projectionPath = string.Empty;
        public string kind = string.Empty;
        public bool bootstrapRuntime;
    }
    [Serializable] internal sealed class RuntimeBindingsDocument
    { public int schemaVersion; public RuntimeBindingDocument[] components = Array.Empty<RuntimeBindingDocument>(); }
    [Serializable] internal sealed class RuntimeBindingDocument
    { public string id = string.Empty; public string repository = string.Empty; public string path = string.Empty; }
    [Serializable] internal sealed class ProjectPackageSetDocument
    {
        public int schemaVersion;
        public string repository = string.Empty;
        public string trackingRef = "main";
        public string resolvedRevision = string.Empty;
        public string managedProjectionRoot = "Assets/CraftyRacoon/Managed";
        public bool needsPublish;
    }
    [Serializable] internal sealed class LocalPackageSetStateDocument
    {
        public int schemaVersion = 1;
        public string checkoutPath = string.Empty;
    }
    [Serializable] internal sealed class OfflineApplyDocument
    {
        public int schemaVersion = 2;
        public string projectRoot = string.Empty;
        public string projectGitRoot = string.Empty;
        public string projectGitDirectory = string.Empty;
        public string packageSetRoot = string.Empty;
        public string packageSetRevision = string.Empty;
        public string projectHead = string.Empty;
        public string indexHash = string.Empty;
        public string gitmodulesHash = string.Empty;
        public string projectBindingHash = string.Empty;
        public string runtimeBindingsHash = string.Empty;
        public string unityExecutable = string.Empty;
        public OfflineApplyEntry[] entries = Array.Empty<OfflineApplyEntry>();
        public OfflineWrite[] writes = Array.Empty<OfflineWrite>();
    }
    [Serializable] internal sealed class OfflineApplyEntry
    {
        public string identity = string.Empty;
        public string sourcePath = string.Empty;
        public string originalPath = string.Empty;
        public string projectionPath = string.Empty;
        public string sourceRepository = string.Empty;
        public string observedRepository = string.Empty;
        public string sourceRevision = string.Empty;
        public string exactPin = string.Empty;
        public string contentHash = string.Empty;
        public string originalContentHash = string.Empty;
        public string originalInventoryHash = string.Empty;
        public string originalMetaHash = string.Empty;
        public string submoduleSection = string.Empty;
        public string projectionState = string.Empty;
        public bool needsPublish;
        public OfflineFile[] files = Array.Empty<OfflineFile>();
        public OfflineMetadata[] metadata = Array.Empty<OfflineMetadata>();
        public OfflineProjectionExclusion[] projectionExclusions = Array.Empty<OfflineProjectionExclusion>();
    }
    [Serializable] internal sealed class OfflineFile
    { public string path = string.Empty; public string mode = "100644"; public string sourceBlob = string.Empty; }
    [Serializable] internal sealed class OfflineMetadata
    {
        public string path = string.Empty;
        public string projectHash = string.Empty;
        public string sourcePreviousHash = string.Empty;
        public string contentBase64 = string.Empty;
    }
    [Serializable] internal sealed class OfflineProjectionExclusion
    {
        public string path = string.Empty;
        public string sourceBlob = string.Empty;
        public string sourceHash = string.Empty;
        public string pairedPath = string.Empty;
        public string reason = string.Empty;
    }
    [Serializable] internal sealed class OfflineWrite
    { public string path = string.Empty; public string previousHash = string.Empty; public string content = string.Empty; }
}
