# module.verification.state-snapshot

Host-published immutable state snapshots with stable channel-local capture identity and bounded retention.

`Read` never performs capture. A host publishes at its own safe consistency boundary, and readers can only read the latest or a specifically retained snapshot. Capture failures are retained without replacing them with a partial snapshot.

Public vocabulary is `StateSnapshotChannel`, `StateSnapshotReference`, `StateSnapshotReferenceCodec`, `StateSnapshotRead<TSnapshot>`, `IStateSnapshotReader<TSnapshot>` and `IStateSnapshotPublisher<TSnapshot>`. This module does not define facts or event metadata.

The reference contains only channel and capture identity. Tick, source, scope, epoch, trace and correlation data belong to the immutable product snapshot when they are part of that product's semantics. The channel publishes and retains the supplied payload reference but never mutates it; callers must supply immutable payloads.

`StateSnapshotReferenceCodec` defines the canonical text representation of a reference as `<channel-id-N>:<capture-id>`. The channel identity uses the 32-digit GUID `N` format and the capture identity uses invariant-culture decimal digits. Decoding rejects empty channel identities, non-positive capture identities and non-canonical values.

This public contract is frozen. The module does not dispatch facts, evaluate diagnostics or oracles, retain traces or evidence, or own replay history.

The assembly targets `netstandard2.1`, uses C# 9, has no Unity, Simulation, Playback or I/O dependency, and keeps a bounded in-memory history.
