# Project development initialization

## Product surface

The Unity host exposes exactly two Package Management workflows:

- **Create PackageSet** creates a new external PackageSet from explicit Project source selections.
- **Use Existing PackageSet** converts a Project to the inventory already declared by one immutable PackageSet revision.

Open-local, exact-revision acquisition and one-time tracking-ref resolution are strategies inside **Use Existing PackageSet**. Existing inventory always comes from immutable PackageSet authority; it has no Project source candidate list or prepare-time Project mutation mode.

`Module.PackageManagement` owns portable layout and backing semantics. `Framework.PackageManagement` owns the two workflows, deterministic planning, aggregate conflict rejection and pipeline order. This Unity specialization owns Git, NTFS, Unity metadata, Editor UI and offline transaction details.

## Fixed pipeline and mutation boundary

Both workflows follow:

```text
Observe -> Validate -> Acquire/Resolve -> Plan -> Prepare -> Offline Preflight -> Apply -> Reopen/Verify
```

Before the offline helper starts, the adapter may:

- create or populate an external PackageSet checkout;
- for Create only, create source repositories and commit the new PackageSet;
- initialize existing PackageSet source checkouts at already-declared gitlinks;
- write immutable plan/helper/hash evidence under the actual Project Git directory.

Before Apply, it must not modify Project tracked files, the Project index, Project source directories, Junctions or `.meta` files. The Framework re-observes Project HEAD, index, tracked-content, metadata and Junction evidence after export; any difference rejects the preparation.

The copied PowerShell helper is the only cutover owner. It performs preflight after Unity exits, retains backups and a journal, creates Junctions, stages ordinary Project blobs, writes binding files, replaces the Project index and records local checkout state. A used plan cannot be replayed.

## Shared and local configuration

Portable Project configuration is committed at:

```text
PackageManagement/project.packageset.json
```

Schema 3 contains the PackageSet repository, tracking ref, immutable resolved revision, managed projection root and `needsPublish` state. The exact revision changes only after a validated offline apply.

Machine-local checkout state is stored outside the Project worktree at:

```text
<actual Project gitdir>/PackageManagement/local-checkout.json
```

Its contract is:

```json
{
  "schemaVersion": 1,
  "checkoutPath": "<absolute PackageSet checkout>"
}
```

Apply writes that record only after the Project transaction succeeds. Prepare does not write it early.

## Use Existing PackageSet

Choose one acquisition strategy:

1. **Open local checkout**: use a clean independent checkout at its current HEAD. If Project configuration already has a resolved revision, HEAD must match it.
2. **Clone/open exact revision**: clone or reuse the configured repository and detach at the configured immutable revision.
3. **Resolve tracking ref once**: resolve the configured ref to one exact object id, acquire only that revision and persist it only through successful Apply.

The selected PackageSet is authoritative per entry:

- `package-set.json` supplies identity, canonical source path, canonical projection path and kind;
- `.gitmodules` supplies the portable source repository identity;
- the PackageSet tree gitlink supplies the exact source revision;
- the canonical source checkout must be clean, independently rooted, at that exact revision and have exactly the declared origin.

Use Existing accepts only portable `GitSubmodule` entries. A `LocalDraft` must be published and converted to a declared remote-backed source before another Project can use it. Neither acquisition nor planning edits `package-set.json`, `.gitmodules`, gitlinks or PackageSet commits. Fingerprints of all three authority surfaces are checked before planning and again before export.

### Project input whitelist

For every authoritative entry, the Project is classified as one of:

- `Absent`: the canonical projection and its root metadata are absent, with no related legacy Project submodule or gitlink left elsewhere under `Assets`;
- `PhysicalSnapshot`: the canonical ordinary snapshot matches the exact source content;
- `BootstrapGitlink`: a declared runtime entry maps to one clean Project submodule whose repository, Project gitlink and HEAD all match the PackageSet authority;
- `CorrectJunction`: the canonical projection already targets the exact canonical source;
- `Conflict`: occupied, ambiguous, dirty, mismatched or independently edited state.

The adapter derives the mapping from each manifest entry, canonical identity policy and repository/gitlink evidence. It does not special-case a Project name or hard-code the historical `Assets/PackageManagement` location. Bootstrap mapping is available only for the three canonical runtime identities and only when their evidence is unique. Package Manager runtime entries must be either zero or all three; a partial set is rejected.

`Absent`, `PhysicalSnapshot`, `BootstrapGitlink` and `CorrectJunction` are observations only during Prepare. They become schema-2 plan records; Prepare never “fixes” any of them.

## Create PackageSet

Create requires an absent or provably empty external destination. The UI provides explicit controls to read Project submodules, add a local folder, or include the three Package Manager runtime repositories. Nothing is automatically included.

Each selection must have a canonical `Framework.*`, `Module.*` or `Workspace.*` identity. Framework policy derives both source and projection buckets; the Unity adapter does not duplicate those rules. Git sources must be clean independent checkouts with a portable origin and an exact Project gitlink/HEAD. Runtime may use the currently compiled clean checkout only as an explicit three-entry selection. Dirty, overlapping, nested, nonportable or ambiguously owned sources are rejected together before PackageSet preparation.

A selected folder without Git metadata can become a `LocalDraft`. Create commits a local baseline under PackageSet-owned Git metadata and marks the result `NeedsPublish`. Existing workflows reject that entry until it has a portable repository and gitlink.

Create writes a schema-2 `package-set.json`, canonical sources and exact gitlinks, then commits the new PackageSet. It still does not touch Project paths or index until offline Apply.

## Runtime composition

Runtime location discovery uses:

```text
runtime.bindings.json
    -> bootstrap.plan.json when bindings are absent
    -> transient self-observation when both durable files are absent
```

Malformed durable evidence is an error and does not fall through. Self-observation locates the repositories that own the assemblies currently compiled by Unity, then validates independent roots, portable origins and exact clean HEADs. It is location evidence for explicit Create selection or declared Existing-entry mapping; it never adds runtime entries to a PackageSet.

When a plan contains all three declared runtime identities, Apply writes `runtime.bindings.json`. Those records contain repository and Project path, while PackageSet gitlinks remain revision authority.

## Offline plan and safety

Schema-2 `plan.json` records Project/Git roots, PackageSet exact revision, Project HEAD/index/`.gitmodules` fingerprints, one entry per deterministic identity, and approved metadata writes. Each entry records source/original/projection paths, source revision, content hash, root `.meta` hash, Project submodule section, projection state and explicit ordinary-file inventory.

Offline preflight rejects stale or unsafe state before cutover, including:

- changed Project HEAD, index, `.gitmodules`, source HEAD or source content;
- changed PackageSet HEAD or dirty PackageSet;
- path escape, reparse ancestor, occupied Absent path or changed Junction target;
- physical snapshot mismatch, missing Bootstrap submodule ownership or `.meta` mismatch;
- incompatible Git attributes/filters, index lock or a replayed operation.

The helper retains `journal.json` and backups. `preflight-failed` means mutation did not start. A caught failure after mutation begins attempts rollback. `needs-recovery` requires human review; do not delete or replay that operation.

## Automated contracts

Run on Windows NTFS with Windows PowerShell 5.1:

```powershell
.\tests\WorkflowSurface.Contracts.ps1
dotnet run --project .\tests\StrictJson.Contracts\StrictJson.Contracts.csproj
.\tests\OfflineApply.Contracts.ps1
```

The workflow surface contract checks the breaking API boundary, split environment ports, read-only Existing implementation and schema contracts. The strict JSON contract rejects duplicate, legacy, unknown and wrong-typed members on every durable JSON authority surface. Offline contracts use real Git and NTFS Junction fixtures for preflight, apply, replay rejection and rollback behavior.

For the real-Project Prepare gate, Unity batchmode can invoke the same Existing environment and application service used by the Editor window:

```powershell
& $UnityExe -batchmode -nographics -quit -projectPath $ProjectRoot `
  -executeMethod CraftyRacoon.PackageManagement.Unity3D.Editor.Development.UseExistingPackageSetPrepareCommand.Run `
  --pm-package-set-root $PackageSetRoot --pm-prepare-output $PrepareResult
```

`$PackageSetRoot` and `$PrepareResult` must be absolute paths. The result must be outside the Project Git tree, inside its private Git directory, or covered by the Project ignore policy. The command always uses `OpenLocalCheckout`; repository, exact revision and projection authority come from the Project's schema-3 declaration. It writes a deterministic JSON summary containing the operation/plan path, exact revision, `AlreadyConfigured` result and identity-sorted entry evidence. Any exception exits Unity with code 1. This command prepares and reports only; it never runs offline Apply.

## Required Unity gates

Automated contracts do not replace real Unity validation. Before release, use an isolated worktree of the actual target Project and verify:

| Gate | Required result |
| --- | --- |
| Prepare invariants | Worktree, index, `.meta` hashes and Junction targets unchanged; only external checkout/private operation evidence added |
| Plan review | Every identity, origin, exact pin, path, state and write matches PackageSet authority |
| Offline Apply | Journal applied; backups retained; ordinary blobs staged; GUID metadata preserved |
| Reopen | Correct Unity version imports and compiles without Console errors |
| Runtime | All three declared runtime bindings resolve to canonical sources, or zero runtime entries remain on bootstrap topology |
| Local state | `local-checkout.json` schema 1 round-trips after restart |
| Fresh clone | Reviewed Project snapshot compiles without a local PackageSet checkout or Junctions |

If source authority, metadata ownership, an existing journal or user data cannot be proven safe, stop the affected mutation and preserve evidence. Do not add a general reconciler to force the Project through.
