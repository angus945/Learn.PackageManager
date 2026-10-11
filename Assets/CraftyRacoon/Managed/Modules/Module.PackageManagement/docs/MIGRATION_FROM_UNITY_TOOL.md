# Extraction from Tool.PackageManager.Unity3D

## Existing source

The first implementation lives in `Tool.PackageManager.Unity3D` under:

```text
Editor/
  Domain/
  Inspection/
  Manifest/
  Infrastructure/
  UI/
```

The extraction should preserve current Unity behavior while moving only host-neutral semantics first.

## Move to Module.PackageManagement

Conceptually migrate:

```text
ManagedPackageDefinition
ManagedPackageRequirement
ManagedPackageSnapshot
ManagedRequirementSnapshot
ManagedPackageState
ManagedRequirementState
PackageStateReducer
package inspection composition
PackageSet discovery state
snapshot alignment rules
```

The Module version intentionally changes requirement extensibility from a closed `ManagedRequirementKind` enum to `ProviderId + Parameters`.

## Keep in Unity Host

Keep these concerns outside the neutral Module:

```text
Application.dataPath
UnityEditor.PackageManager.PackageInfo
UnityEditor.EditorWindow
UnityEngine.JsonUtility
Unity Package Manager inspection
Unity project-root resolution
Unity UI
```

The existing `ProjectInspectionContext` should not move into this repository.

## Host adapter direction

The Unity ProductRoot should eventually compose inspectors such as:

```text
git.submodule
filesystem.path
unity.upm
external.distribution
```

and then construct:

```text
RequirementInspectionDispatcher
        ->
PackageInspector
```

A pure .NET Host can compose the same core with its own inspector set.

## Next extraction slice

Before adding new PackageSet mutation to the Unity Editor assembly:

1. consume `Module.PackageManagement` from the Unity host;
2. adapt current package manifests into `ManagedPackageDefinition`;
3. implement current requirement mechanisms as Host inspectors;
4. prove the existing Unity read-only window still reports the same package states;
5. add PackageSet Discovery + Project-to-PackageSet Mapping on the neutral core boundary.

Do not move install/update/repair orchestration into this Module.
