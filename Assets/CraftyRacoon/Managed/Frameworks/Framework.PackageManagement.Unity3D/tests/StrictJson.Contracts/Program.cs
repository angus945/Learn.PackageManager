#if !UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using CraftyRacoon.PackageManagement.Unity3D.Editor.Development;

internal static class Program
{
    private static int checks;

    private static void Main()
    {
        string project = "{\"schemaVersion\":3,\"repository\":\"https://example.invalid/package-set.git\",\"trackingRef\":\"main\",\"resolvedRevision\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"managedProjectionRoot\":\"Assets/Managed\",\"needsPublish\":false}";
        CheckProject(project, true, "strict Project binding accepts the exact schema");
        CheckProject("\uFEFF" + project, false, "strict JSON rejects an encoding signature outside the durable handoff boundary");
        CheckProject(project.Replace("\"schemaVersion\":3", "\"schemaVersion\":3,\"schemaVersion\":3"), false, "strict Project binding rejects duplicate members");
        CheckProject(project.Replace("\"needsPublish\":false", "\"needsPublish\":false,\"packageSetRevision\":\"legacy\""), false, "strict Project binding rejects unknown legacy members");
        CheckProject(project.Replace("\"needsPublish\":false", "\"needsPublish\":\"false\""), false, "strict Project binding rejects wrong member types");
        string runtime = "{\"schemaVersion\":1,\"components\":[{\"id\":\"Module.PackageManagement\",\"repository\":\"https://example.invalid/module.git\",\"path\":\"Assets/Managed/Module.PackageManagement\"}]}";
        CheckRuntime(runtime, true, "strict runtime binding accepts the exact schema");
        CheckRuntime(runtime.Replace("\"id\":", "\"id\":\"duplicate\",\"id\":"), false, "strict runtime binding rejects duplicate component members");
        CheckRuntime(runtime.Replace("\"path\":", "\"legacy\":true,\"path\":"), false, "strict runtime binding rejects unknown component members");
        CheckRuntime(runtime.Replace("\"components\":[", "\"components\":{\"items\":[").Replace("]}", "]}}"), false, "strict runtime binding rejects a non-array components member");
        string manifest = "{\"schemaVersion\":2,\"identity\":\"PackageSet.Test\",\"target\":{\"identity\":\"Project.Test\",\"repository\":\"https://example.invalid/project.git\"},\"sourceRoot\":\"Sources\",\"managedProjectionRoot\":\"Assets/Managed\",\"entries\":[{\"identity\":\"Module.Test\",\"sourcePath\":\"Sources/Modules/Module.Test\",\"projectionPath\":\"Modules/Module.Test\",\"kind\":\"GitSubmodule\",\"bootstrapRuntime\":false}]}";
        CheckPackageSet(manifest, true, "strict PackageSet manifest accepts the exact schema");
        CheckPackageSet(manifest.Replace("\"projectionPath\":", "\"projectionPath\":\"duplicate\",\"projectionPath\":"), false, "strict PackageSet manifest rejects duplicate entry members");
        CheckPackageSet(manifest.Replace("\"projectionPath\":", "\"projectionName\":\"legacy\",\"projectionPath\":"), false, "strict PackageSet manifest rejects legacy projectionName");
        CheckPackageSet(manifest.Replace("\"bootstrapRuntime\":false", "\"bootstrapRuntime\":\"false\""), false, "strict PackageSet manifest rejects wrong entry types");
        CheckPackageSet(manifest.Replace("\"repository\":", "\"legacy\":true,\"repository\":"), false, "strict PackageSet manifest rejects unknown target members");
        string localCheckout = "{\"schemaVersion\":1,\"checkoutPath\":\"P:/PackageSet\"}";
        CheckLocalCheckout(localCheckout, true, "strict local checkout accepts the exact schema");
        CheckLocalCheckout(localCheckout.Replace("\"checkoutPath\":", "\"packageSetRoot\":\"legacy\",\"checkoutPath\":"), false, "strict local checkout rejects legacy members");
        CheckLocalCheckout(localCheckout.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), false, "strict local checkout rejects duplicate members");
        Console.WriteLine("PASS: " + checks + " strict Package Management JSON contracts");
    }

    private static void CheckProject(string json, bool expected, string name)
    {
        List<string> diagnostics = new List<string>();
        bool actual = StrictBindingJsonShape.ValidateProject(json, diagnostics);
        Check(actual == expected, name);
    }

    private static void CheckRuntime(string json, bool expected, string name)
    {
        List<string> diagnostics = new List<string>();
        bool actual = StrictBindingJsonShape.ValidateRuntime(json, diagnostics);
        Check(actual == expected, name);
    }

    private static void CheckPackageSet(string json, bool expected, string name)
    {
        List<string> diagnostics = new List<string>();
        bool actual = StrictBindingJsonShape.ValidatePackageSet(json, diagnostics);
        Check(actual == expected, name);
    }

    private static void CheckLocalCheckout(string json, bool expected, string name)
    {
        List<string> diagnostics = new List<string>();
        bool actual = StrictBindingJsonShape.ValidateLocalCheckout(json, diagnostics);
        Check(actual == expected, name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        checks++;
    }
}
#endif
