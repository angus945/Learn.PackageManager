# Framework.PackageManagement.Unity3D Architecture

## Responsibility

This repository is the Unity Host specialization for the generic Package Management stack.

```text
Unity Editor / Unity project
        |
        | Unity-specific discovery + adapters + composition
        v
Framework.PackageManagement.Unity3D
        |
        | generic workflow
        v
Framework.PackageManagement
        |
        | semantics
        v
Module.PackageManagement
```

It must not become a second Package Management framework.

## Deployment model

There is no UPM layer in this architecture.

`Tool.PackageManager` installs the Package Management repositories directly as Git submodules inside the target Unity project's `Assets` tree.

The generic repositories keep their existing .NET project files and also expose minimal Unity assembly definitions:

```text
Module.PackageManagement
  src/Module.PackageManagement/Module.PackageManagement.asmdef

Framework.PackageManagement
  src/Framework.PackageManagement/Framework.PackageManagement.asmdef
      ↓ references Module.PackageManagement

Framework.PackageManagement.Unity3D
  Editor/Framework.PackageManagement.Unity3D.Editor.asmdef
      ↓ references both generic assemblies
```

No source copying, generated package projection, or `Packages/manifest.json` entry is required.

## Source authority

The Unity specialization must not infer dependency checkouts from sibling names or fixed folders.

The first source topology is selected by the user during bootstrap and recorded at:

```text
<ProjectGitRoot>/PackageManagement/bootstrap.plan.json
```

U1 resolves these component ids from that handoff:

- `Module.PackageManagement`
- `Framework.PackageManagement`
- `Framework.PackageManagement.Unity3D`

The handoff is an initial observation/migration input, not permanent PackageSet or Project configuration authority. After conversion, `project.packageset.json` selects one exact PackageSet revision, while that PackageSet's manifest, `.gitmodules` repositories and gitlinks are per-entry authority.

## Unity source-root constraint

For the Unity bootstrap plan, the selected initial source root must be inside the target Unity project's `Assets` tree.

This is a host activation constraint, not a fixed folder convention. The user may choose any suitable folder below `Assets`.

Examples:

```text
Assets/CraftyRacoon/PackageManagement
Assets/External/CraftyRacoon
Assets/Infrastructure/PackageManagement
```

All three Package Management repositories are installed beneath that selected root as sibling submodules.

## Mutation boundary

U1 is read-only after bootstrap.

It does not:

- clone or fetch repositories;
- change Git revisions;
- initialize or repair unrelated submodules;
- relocate sources;
- create Junctions/symlinks;
- materialize distributions;
- modify PackageSet state.

Those mutations remain downstream of generic Framework workflows and explicit Unity adapters.

## V2 workflow boundary

The product surface contains only **Create PackageSet** and **Use Existing PackageSet**.

Create is wired through `IPackageSetCreator` and `ICreatePackageSetEnvironment`. Its environment may initialize sources and commit a new external PackageSet. Use Existing is wired through `IProjectPackageSetConverter` and `IUseExistingPackageSetEnvironment`. Its narrower environment can observe/validate the Project, acquire an exact external checkout, read authoritative layout, observe declared entries and export an offline plan. It has no method that can add a source, finalize a PackageSet or commit its inventory.

The Unity adapter fingerprints Project HEAD, index, tracked changes, `.meta` files and Junction targets before planning. Framework requires the same observation after export or an AlreadyConfigured no-op. Existing PackageSet manifest, `.gitmodules` and gitlinks are fingerprinted around source acquisition and export. All Project path, metadata and index changes belong to the offline helper after Unity exits.

## Editor surface

The Unity integration is exposed as a project-scoped Settings provider:

```text
Project Settings
└─ CraftyRacoon Packages
```

The Settings surface remains primarily diagnostic and links to a narrow initialization window. The previous general `Tools` menu and discovery/repair surfaces are not retained.

Its job is to verify the actual runtime composition, show the durable binding source when one exists, validate Unity `Assets` visibility, report typed Module/Framework availability, and launch the two explicit workflows. It does not fabricate a PackageSet discovery state.
