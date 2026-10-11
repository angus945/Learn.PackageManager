# Framework.PackageManagement

Reusable package-management workflow owner.

This Framework orchestrates package-management use cases over the semantic model in `Module.PackageManagement`. It is host-neutral and does not own Unity, Blazor, Git CLI, filesystem, Junction, or UI implementations.

## Current slices

### Package Management V2 workflows

The public mutation surface has exactly two product use cases:

- `IPackageSetCreator.Prepare(CreatePackageSetRequest, CancellationToken)` creates a new PackageSet from explicit Project selections.
- `IProjectPackageSetConverter.Prepare(UseExistingPackageSetRequest, CancellationToken)` converts a Project to an existing PackageSet without changing that PackageSet's inventory, manifest, or gitlinks.

The workflows use separate environment ports. `IUseExistingPackageSetEnvironment` can observe and validate the Project, acquire or resolve an exact PackageSet revision, read its authoritative layout, observe declared entries, and export an offline plan. Authoritative entry repositories and source gitlinks are compared with the observed checkout before export. The port has no Create, source-preparation, manifest-write, or PackageSet-commit capability.

Both workflows execute the fixed sequence `Observe -> Validate -> Acquire/Resolve -> Plan -> Prepare`. Plans are deterministic by package identity. All observed entry conflicts are reported together, and no offline plan is exported when any conflict exists. A final observation must match the planned Project HEAD, index fingerprint, and evidence token before preparation can return successfully.

### F1 — PackageSet Discovery

- Project / target -> PackageSet binding resolution;
- PackageSet discovery workflow;
- reduction to `NotConfigured / Missing / Invalid / Available / Ready`;
- explicit semantic ports for host/environment observations.

### F2 — Snapshot Observation

For every managed source declared by the PackageSet inventory:

```text
Canonical Source HEAD
PackageSet Exact Pin
Project Distribution Projection
        ↓
SnapshotAlignmentEvaluator
        ↓
Unknown / Aligned / SourceDirty / SourcePinMismatch / ProjectionMismatch
```

Projection comparison is intentionally skipped until the canonical source is clean and its HEAD matches the exact PackageSet pin.

F1 and F2 remain read-only. V2 workflow preparation may create or acquire external checkouts and persist private operation evidence, but Project files, metadata, Junctions, and the Project Git index remain reserved for the separately journaled offline Apply transaction.

## Dependency composition

`Framework.PackageManagement` depends architecturally on `Module.PackageManagement`, but this repository does not embed or pin another source checkout.

The build caller must provide:

```text
PackageManagementModuleProject=<path to Module.PackageManagement.csproj>
```

This keeps source/revision selection outside the Framework repository and avoids guessing sibling checkout layouts.

See `docs/ARCHITECTURE.md`.

## Unity direct-submodule compilation

For Unity products that compose Package Management through direct Git submodules, `src/Framework.PackageManagement/Framework.PackageManagement.asmdef` exposes the same source as the `Framework.PackageManagement` Unity assembly and references `Module.PackageManagement`. Unity-specific adapters and Editor composition remain owned by `Framework.PackageManagement.Unity3D`; this repository stays host-neutral.
