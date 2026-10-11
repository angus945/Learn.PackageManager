# Roadmap

## F1 — PackageSet Discovery

Status: baseline implemented.

Capabilities:

- target-root validation;
- explicit target -> PackageSet binding read;
- configured checkout existence;
- PackageSet manifest/source readiness inspection;
- reduction to the Module's discovery state.

Exit criterion:

A host-neutral caller can provide adapters and obtain PackageSet identity, local checkout, and `NotConfigured / Missing / Invalid / Available / Ready` without Unity dependencies.

## F2 — Snapshot Observation

Status: baseline implemented.

Capabilities:

- explicit PackageSet snapshot inventory;
- canonical source HEAD / dirty observation;
- PackageSet exact pin passed from inventory;
- distribution projection comparison;
- ordered short-circuit before projection comparison;
- reduction through `SnapshotAlignmentEvaluator`.

Result states:

```text
Unknown
Aligned
SourceDirty
SourcePinMismatch
ProjectionMismatch
```

Exit criterion:

A host-neutral caller can provide F2 adapters and obtain alignment for every managed source without filesystem, Git, Unity, or platform APIs leaking into the Framework.

No mutation is allowed in F2.

## F3 — Package Inspection Migration

Move reusable orchestration from `Tool.PackageManager.Unity3D` to this Framework while leaving Unity-specific requirement inspectors outside.

Exit criterion:

The existing Unity read-only UI can consume Framework results without owning inspection workflow.

## F4 — PackageSet Acquisition

Status: V2 workflow contract implemented; Host adapters and real-Project gates remain.

- resolve a tracking ref at most once when no exact revision exists;
- acquire or open the selected checkout at one immutable revision;
- read manifest and gitlinks as the only Existing inventory authority;
- fail before planning on credential, identity, or revision errors.

## F5 — Development Backing

Status: V2 deterministic planning contract implemented; offline Host transaction remains platform-specific.

- classify Absent, PhysicalSnapshot, BootstrapGitlink, CorrectJunction, and Conflict states;
- verify source, pin, content, metadata ownership, and path ownership;
- aggregate all entry conflicts before export;
- return a read-only `AlreadyConfigured` result when every projection is already correct.

NTFS Junction is one adapter implementation, not a Framework semantic.

## F6 — Projection Management

- execute one immutable, journaled offline plan;
- materialize only an explicitly validated Absent projection;
- convert a validated physical snapshot or bootstrap gitlink;
- preserve metadata and stage ordinary Project blobs.

Arbitrary mutation, reconciliation, and mixed-layout recovery are not planned as Package Management V2 product workflows.

## Deferred

- generic SAT solver;
- automatic latest updates;
- arbitrary install scripts;
- framework self-bootstrap;
- Tool.PackageManager bootstrap implementation.
