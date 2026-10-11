# Package Management V2 preparation workflows

## Public use cases

Framework.PackageManagement exposes two product workflows:

- `Create PackageSet` through `IPackageSetCreator.Prepare(CreatePackageSetRequest, CancellationToken)`.
- `Use Existing PackageSet` through `IProjectPackageSetConverter.Prepare(UseExistingPackageSetRequest, CancellationToken)`.

They are separate workflows with separate requests, plans, preparations, and environment ports. Source acquisition choices are internal strategies of `Use Existing PackageSet`; they are not additional product modes.

## Authority boundary

`CreatePackageSetRequest` contains the new PackageSet identity and destination plus explicitly selected Project sources. Framework canonicalizes every selected source into `Frameworks/`, `Modules/`, or `Workspaces/` buckets. The Host returns a typed source observation with its repository identity; GitSubmodule selections require a portable repository plus exact source HEAD and pin, while an unpublished LocalDraft has an exact local HEAD and remains `NeedsPublish`.

`UseExistingPackageSetRequest` contains only:

- the Project root;
- PackageSet checkout path and repository;
- tracking ref and optional pre-resolved exact revision;
- acquisition strategy.

It has no Project-supplied placements, source selections, source root, or projection root. `IUseExistingPackageSetEnvironment.ReadAuthoritativeLayout` obtains both roots plus every entry, source repository, and exact source revision from the resolved PackageSet manifest, `.gitmodules`, and gitlinks. The Host may map a declared entry to an observed Project source path, but it cannot introduce another identity or change the declared source, projection, repository, revision, kind, or runtime classification. Existing `LocalDraft` entries are rejected until they are published as exact Git submodules.

## Fixed pipeline

Both workflows execute the same ordered stages:

```text
Observe -> Validate -> Acquire/Resolve -> Plan -> Prepare
```

1. Observe captures the Project HEAD, index fingerprint, and Host evidence without mutation.
2. Validate reports all Project-level precondition failures before acquisition.
3. Acquire/Resolve creates or opens an external checkout and returns an immutable exact revision when applicable.
4. Plan sorts entries by ordinal identity, observes every declared entry, applies Module semantic policy, validates canonical paths, and aggregates all conflicts.
5. Prepare may commit a newly created external PackageSet and may export private offline operation evidence. It then re-observes the Project and rejects any HEAD, index, or evidence-token change. A failed acquisition, preparation, or export is audited the same way; if both the workflow and invariant fail, both errors are retained.

Any conflict prevents PackageSet preparation for Create and prevents offline-plan export for both workflows. A plan is never partially exported.

## Capability separation

`ICreatePackageSetEnvironment` may acquire a new external workspace, prepare canonical sources, write its new manifest and gitlinks, and commit the new PackageSet before exporting an offline Project plan.

`IUseExistingPackageSetEnvironment` can only:

- observe and validate Project facts;
- acquire or resolve the requested PackageSet checkout;
- read its authoritative layout;
- observe its declared entries in the Project;
- export an offline plan.

The Existing port has no method that can create a PackageSet, add or prepare a source, write a manifest, stage a PackageSet gitlink, or finalize/commit a PackageSet. This is enforced by its public type surface, not by a runtime mode flag.

## Deterministic plan and runtime composition

Plan entries are always ordered by canonical identity. Canonical source and projection paths come from `PackageSourceDirectoryPolicy`. Each Existing plan entry records the authoritative and observed repository identities and rejects any ordinal mismatch before export.

Package Manager runtime composition must contain either none of the runtime entries or exactly these three:

- `Module.PackageManagement`
- `Framework.PackageManagement`
- `Framework.PackageManagement.Unity3D`

A partial or unexpected runtime composition is a conflict. `Use Existing PackageSet` never appends missing runtime entries to make the set pass.

## Project mutation boundary

Preparation may retain an external checkout and private operation evidence for diagnosis. It must not change Project tracked files, the Project Git index, Project Junctions, `.meta` files, or Project source directories.

The Host's offline transaction is solely responsible for revalidating the immutable plan after the editor exits, journaling backups, applying the approved cutover, rolling back caught failures, and reopening the Host for verification. A fully correct Existing topology returns `AlreadyConfigured` with no offline plan.

## Directory ownership

The canonical roots are configurable, but the Framework owns the buckets beneath them:

```text
<SourceRoot>/
├─ Frameworks/Framework.*
├─ Modules/Module.*
└─ Workspaces/Workspace.*

<ProjectionRoot>/
├─ Frameworks/Framework.*
├─ Modules/Module.*
└─ Workspaces/Workspace.*
```

Flat legacy locations, overlaps, path traversal, and identities outside these three namespaces are rejected. They are not silently reconciled.

## Validation

Run the Release contracts against the exact Module checkout selected by the integration authority:

```powershell
dotnet run --project tests/Development.Contracts/Development.Contracts.csproj `
  --configuration Release `
  -p:PackageManagementModuleProject=<absolute Module.PackageManagement.csproj>

pwsh -File tests/BreakingApi.Contracts.ps1 `
  -ModuleProject <absolute Module.PackageManagement.csproj>
```

The positive contracts verify request authority separation, deterministic ordering, pipeline sequencing, conflict aggregation, conflict export suppression, runtime all-or-none validation, and read-only `AlreadyConfigured` behavior. The isolated negative compilation contract verifies that removed V1 modes, requests, and Existing adoptions cannot compile. Platform-specific transaction and real-Project validation remain mandatory in the Host specialization.
