# Framework.PackageManagement Architecture

## Responsibility

This Framework owns reusable package-management workflow and orchestration.

The semantic model and invariant reduction remain owned by `Module.PackageManagement`.

```text
Tool / CLI / .NET Host / Unity integration
        |
        | composition + adapters
        v
Framework.PackageManagement
        |
        | workflow
        v
Module.PackageManagement
```

## Phase F1 — PackageSet Discovery

```text
Target Root
    ↓
Validate target
    ↓
Read Project/Target -> PackageSet binding
    ↓
Inspect declared local checkout
    ↓
Inspect PackageSet manifest/source readiness
    ↓
Module.PackageManagement evaluator
    ↓
NotConfigured / Missing / Invalid / Available / Ready
```

The Framework owns the ordering and orchestration. The Module owns the status semantics.

F1 ports:

- `ITargetRootValidator`
- `ITargetPackageSetBindingReader`
- `IPackageSetCheckoutInspector`
- `IPackageSetManifestInspector`

## Phase F2 — Snapshot Observation

F2 validates each managed source as a three-way alignment:

```text
Canonical Source HEAD
        │
        ├──────────────┐
        ▼              ▼
source dirty?    PackageSet exact pin
                       │
                       ▼
              HEAD == exact pin?
                       │
                       ▼
          Distribution Projection
                       │
                       ▼
        SnapshotAlignmentEvaluator
```

F2 ports:

- `IPackageSetSnapshotInventoryReader`
- `ICanonicalSourceInspector`
- `IDistributionProjectionInspector`

The inventory reader returns explicit subject identity, canonical source location, distribution projection location, and PackageSet exact pin. The Framework never scans sibling folders or infers these locations.

Projection comparison runs only when:

```text
source is clean
AND source HEAD exists
AND exact pin exists
AND source HEAD == exact pin
```

This prevents a stale or dirty source from being used as the basis of distribution validation.

## Port rule

The Framework does not accept a generic host/service context.

Each Host or adapter implements only the environmental facts it owns. Ports receive semantic inputs rather than Host SDK objects.

## Source authority

This repository deliberately does not:

- embed `Module.PackageManagement` as a submodule;
- pin a Module commit;
- search sibling directories;
- infer dependency locations.

The build receives `PackageManagementModuleProject` explicitly from the composition/build authority.

This follows the PackageSet rule that reusable source selection and exact revision authority must not be duplicated inside consumers.

## Mutation boundary

F1 and F2 perform no mutation.

Package Management V2 adds two explicit preparation workflows:

- `Create PackageSet` may initialize and commit a new external PackageSet from explicit selections.
- `Use Existing PackageSet` treats the existing manifest and gitlinks as read-only authority.

Both workflows follow `Observe -> Validate -> Acquire/Resolve -> Plan -> Prepare`. Prepare may write private operation evidence and copy an offline helper, but it cannot change Project-owned paths, `.meta` files, Junctions, tracked files, or the Project Git index.

Only the journaled offline Apply adapter may perform:

- Junction/symlink creation;
- projection replacement;
- materialization;
- metadata relocation;
- Project binding writes;
- Project index updates.

Generic mutation, arbitrary reconciliation, adding Project dependencies to an existing PackageSet, and update/install queues are outside these workflows.

## Host model

Blazor is currently an ordinary .NET Host. No Blazor-specific Framework fork exists.

Unity-specific APIs and UPM inspection remain adapters outside this repository.
