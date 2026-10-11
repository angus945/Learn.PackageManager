# Phase U1

## Goal

Establish the first closed loop from `Tool.PackageManager` into a Unity project using direct Git submodules only.

## Implemented

- Editor-only Unity specialization assembly.
- Direct references to `Module.PackageManagement` and `Framework.PackageManagement` Unity asmdefs.
- Unity project root discovery.
- Enclosing project Git root discovery.
- `PackageManagement/bootstrap.plan.json` discovery.
- Schema v2 handoff parsing.
- Explicit lookup of all three Package Management component ids.
- Safe resolution of handoff-relative source paths inside the Git root.
- Validation that all three submodules are inside the target Unity project's `Assets` tree.
- Source-entry validation.
- Typed Module and Framework assembly availability checks.
- Runtime composition loaded from durable bindings, bootstrap handoff, or explicit transient self-observation.
- No synthetic PackageSet discovery result when a real binding is unavailable.
- Minimal **Project Settings > CraftyRacoon Packages** provider.

## Expected bootstrap handoff

```json
{
  "schemaVersion": 2,
  "bootstrapper": "Tool.PackageManager",
  "planId": "unity",
  "initialSourceRoot": "<user-selected Assets path>",
  "components": [
    {
      "id": "Module.PackageManagement",
      "repository": "crafty-racoon/Module.PackageManagement",
      "revision": "<exact commit>",
      "path": "<resolved relative path>"
    },
    {
      "id": "Framework.PackageManagement",
      "repository": "crafty-racoon/Framework.PackageManagement",
      "revision": "<exact commit>",
      "path": "<resolved relative path>"
    },
    {
      "id": "Framework.PackageManagement.Unity3D",
      "repository": "crafty-racoon/Framework.PackageManagement.Unity3D",
      "revision": "<exact commit>",
      "path": "<resolved relative path>"
    }
  ]
}
```

## Verification

1. Run `Tool.PackageManager` against the Unity repository.
2. Select the Unity plan.
3. Choose an initial source root under the Unity project's `Assets` tree.
4. Complete bootstrap.
5. Open Unity.
6. Open **Edit > Project Settings > CraftyRacoon Packages**.
7. Confirm all three source bindings are **Ready**.
8. Confirm the Module and Framework assemblies are visible through the typed Unity composition.
9. Confirm the Settings provider shows the actual runtime composition and its durable binding source without reporting a synthetic PackageSet state.

## V2 continuation

U1 remains the bootstrap/runtime-observation baseline. The subsequent V2 layer now adds the two explicit `Create PackageSet` and `Use Existing PackageSet` workflows through separate Framework environment ports. Use Existing treats PackageSet manifest, source repositories and gitlinks as read-only authority; all Project cutover remains in the offline transaction.

Do not introduce UPM distribution, source copying, or a second package-management semantic model.
