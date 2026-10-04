# module.verification.evidence

Immutable evidence manifests, bounded bundle construction and a sink contract.

The module records finite references and metadata for fact streams, state snapshots, evaluations, traces, diagnostics, attachments and replay recordings. It does not duplicate operation state, facts, snapshots, diagnostics or oracle results. The host owns concrete JSON, file, database and CI artifact integrations.

The assembly targets `netstandard2.1`, uses C# 9, and has no Unity, Simulation or Playback dependency.

`EvidenceReference` has value identity. `EvidenceEntry` contains kind, reference, label and copied metadata; it never owns the referenced payload. This public contract is frozen.
