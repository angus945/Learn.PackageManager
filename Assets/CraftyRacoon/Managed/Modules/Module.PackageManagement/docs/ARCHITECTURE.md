# Module.PackageManagement Architecture

## Role

`Module.PackageManagement` is the platform-neutral semantic core for package-management inspection and PackageSet state.

It is a **Module** because it exposes one stable reusable capability: understanding package composition and PackageSet alignment.

It does not own long-running install/update/repair workflows. When those workflows become reusable production behavior, they belong in `Framework.PackageManagement`.

## Dependency direction

```text
ProductRoot / Host
        |
        +-- composition root
        +-- host-specific adapters
        +-- requirement inspectors
        |
        v
Module.PackageManagement
```

The Module has no compile-time dependency on Unity, Blazor, ASP.NET Core, Git CLI, NTFS, or a managed system.

## Host policy

Blazor is currently treated as an ordinary .NET Host.

No `Module.PackageManagement.Blazor` specialization is introduced. A specialization should only be created if a real production constraint produces reusable Blazor-specific integration code.

Unity is also a Host, but Unity-specific APIs such as `UnityEditor.PackageManager` remain outside this repository.

## PackageSet boundary

PackageSet remains an architecture-level authority:

```text
PackageSet
= canonical source selection
+ exact revision pin
+ development backing/projection authority
```

It is not a runtime dependency kind and is not reclassified as a Module.

`Module.PackageManagement` owns reusable semantics that interpret and validate PackageSet state.

## Requirement extension model

A package requirement contains:

```text
Id
DisplayName
ProviderId
Parameters
```

The neutral core does not contain an enum switch for every delivery mechanism.

A Host explicitly registers `IRequirementInspector` implementations:

```text
git.submodule          -> GitSubmoduleRequirementInspector
filesystem.path        -> FileSystemPathRequirementInspector
unity.upm              -> UnityPackageRequirementInspector
external.distribution  -> ExternalDistributionRequirementInspector
```

Each inspector receives its environment-specific dependencies through constructor injection. This keeps the composition root explicit and avoids a generic host service locator.

## PackageSet discovery model

The initial discovery states are:

```text
NotConfigured
Missing
Invalid
Available
Ready
```

`Available` means the configured checkout and manifest exist but at least one readiness condition is not yet satisfied, such as source initialization or exact-revision alignment.

The Module reduces observed facts into the status. The Host is responsible for observing filesystem/Git facts.

## Snapshot alignment model

The Module separately evaluates:

```text
Canonical Source HEAD
PackageSet Exact Pin
Project Distribution Projection
```

The initial result states are:

```text
Unknown
Aligned
SourceDirty
SourcePinMismatch
ProjectionMismatch
```

The Module does not decide how a Host computes projection equality. That observation belongs to the adapter.

## Mutation boundary

The current repository is intentionally read-only.

Do not add these directly to the Module:

- process spawning;
- git clone/submodule mutation;
- Junction or symlink creation;
- filesystem replacement;
- Unity Package Manager mutation;
- operation queues;
- domain-reload continuation.

Reusable multi-step mutation belongs in `Framework.PackageManagement`; platform mechanics remain adapters.
