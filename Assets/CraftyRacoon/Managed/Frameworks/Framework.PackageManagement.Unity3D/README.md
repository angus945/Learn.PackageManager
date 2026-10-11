# Framework.PackageManagement.Unity3D

Unity host specialization for `Framework.PackageManagement`.

## Composition and distribution

`Tool.PackageManager` performs the initial bootstrap by installing these repositories under an explicitly selected Unity `Assets` root:

```text
Module.PackageManagement
Framework.PackageManagement
Framework.PackageManagement.Unity3D
```

The first two remain host-neutral and expose the same source through Unity asmdefs. This specialization provides Unity composition, Editor UI and the Windows Git/filesystem adapters used by the initialization workflow. It is not installed through UPM.

After Project development initialization, the three runtime repositories can be converted only when the selected PackageSet already declares all three canonical runtime identities. The Project retains their ordinary distribution snapshot at its declared managed paths; an engineer's local paths can be backed by Junctions. Standalone `Tool.PackageManager` remains independent of the managed runtime composition.

## Project Settings

Open **Edit > Project Settings > CraftyRacoon Packages**.

The existing runtime diagnostics remain available. Select **Initialize Project Development / PackageSet** to open exactly two product workflows:

- **Create PackageSet**: create a new external PackageSet from explicitly selected reusable Project sources. Runtime sources are never selected implicitly.
- **Use Existing PackageSet**: open a local checkout, acquire an exact remote revision, or resolve a tracking ref once and then use that immutable revision. Its manifest, `.gitmodules`, gitlinks, source repositories and exact pins are read-only authority; the Project cannot add candidates or source selections.

Project Settings reports the runtime composition actually loaded by Unity and identifies its durable binding source. It does not synthesize a PackageSet discovery result when no binding exists.

## Preparation and offline apply

Preparation runs either the neutral `PackageSetCreator` or `ProjectPackageSetConverter`. Create may initialize and commit a new external PackageSet. Use Existing may populate external checkouts at already-declared gitlinks but cannot edit or commit its inventory. Both workflows observe the Project again after preparation and reject any HEAD, index, tracked-content, `.meta`, or Junction evidence change before returning a result.

All Project mutations are deferred to a schema-2 offline plan under the actual Project Git directory. Prepare never retargets a Junction, relocates a Project directory, moves `.meta`, edits tracked Project files, or changes the Project index. A successful apply writes machine-local checkout state as `{ "schemaVersion": 1, "checkoutPath": "..." }`.

Apply requires explicit confirmation and closes Unity. A copied helper outside the managed source tree revalidates the plan, retains original directories and metadata for recovery, creates Junctions, and stages ordinary Project file blobs. This also avoids replacing the Package Manager's own source while its Unity domain is running. Reopen Unity, verify compilation and inspect the staged Project diff before committing. The tool never automatically commits the Project or pushes any repository.

Normal later development does not require switching back to materialized directories before committing a Project snapshot.

## Runtime binding handoff

Initial bootstrap records remain in:

```text
<ProjectGitRoot>/PackageManagement/bootstrap.plan.json
```

Successful declared-runtime conversion writes:

```text
<ProjectGitRoot>/PackageManagement/runtime.bindings.json
<ProjectGitRoot>/PackageManagement/project.packageset.json
```

Runtime location resolution has an explicit priority:

1. `runtime.bindings.json` after declared-runtime conversion;
2. `bootstrap.plan.json` from Tool bootstrap;
3. self-observation from the Package Manager assemblies currently compiled by Unity, only when neither durable file exists.

Self-observation resolves each assembly back to one source root under this Project's `Assets` tree, then reads that checkout's `origin` and exact `HEAD`. It is transient initialization/recovery evidence, not a new durable authority. If either durable file exists but is malformed, resolution fails; it never falls through to self-observation.

Managed runtime bindings supersede the initial topology for location resolution. They do not repeat per-source revision pins. Project snapshot composition references a PackageSet commit; its source gitlinks remain the managed source revision authority. Machine-local checkout paths and apply journals live under the actual Project Git directory, not portable Project metadata.

## Validation and limitations

Use mutually compatible V2 revisions of Module, Framework and this repository together. Existing `Tool.PackageManager` catalog pins are not silently upgraded by this change.

The offline contract workflow runs actual Git and NTFS Junction fixtures on Windows. It is not a substitute for compiling and reopening the real Unity 6000.3.20f1 Project; that remains a manual gate. Local NTFS is the first supported environment. Nested source submodules/symlinks, stale snapshots, independent Project changes, incompatible filters and ambiguous directories fail instead of being flattened or overwritten.

See [Development initialization and manual validation](docs/DEVELOPMENT_INITIALIZATION.md). `docs/PHASE_U1.md` describes the earlier bootstrap-only baseline; its non-goals are not a claim that the new explicit initializer is absent.
