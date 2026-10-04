# Diagnostic contracts

## Ownership and identity

`DiagnosticDescriptor` identifies a diagnostic kind by the ordinal,
case-sensitive `(Producer, Code)` pair. Both strings are required and cannot
be blank. Equality and hashing use that pair only; translation, category,
message key and default severity do not change identity. Producers own code
meaning and must not reuse an established code for a different problem.
Existing module codes remain owned by their original producers.

`Diagnostic.Severity` defaults to the descriptor value and can explicitly
override it. The only core values are `Info`, `Warning` and `Error`. Severity
does not define operation success, failure, cancellation or retry behavior.
`Message` is a required non-null fallback string and never an identity or
deduplication key. `MessageArguments` is an ordered list of strings; the core
does not interpret or format it.

All data classes are sealed with getter-only properties. Dictionaries and
lists are copied into private storage and exposed through read-only wrappers.
Collection elements are strings or other immutable core objects. Null
collection arguments become empty collections; null elements, dictionary keys
and dictionary values are rejected. Dictionary keys use ordinal comparison.
The core accepts no exceptions, arbitrary objects, delegates or engine objects.

## Sources and coordinates

`Diagnostic.Location` and `DiagnosticLocation.TextSpan` are independently
optional. Missing source information remains null. An absent range is distinct
from `Start = 0`, and an absent line is distinct from `Line = 1`.

`ResourceId` is an opaque stable resource identifier. `ResourceRevision` is an
opaque version identifier. `FieldPath` follows the producer's documented
structured-data convention. `DisplayPath` is a display hint and never replaces
resource identity. All four are optional strings; the core does not infer or
normalize resource identity from a path.

`DiagnosticTextSpan.Start` is a zero-based UTF-16 code-unit offset. `Length`
uses the same units and `End = Start + Length` is exclusive. Empty spans are
valid; negative and overflowing ranges are rejected. Optional `Line` and
`Column` are one-based, with column counted in UTF-16 code units. Each provided
coordinate must be positive. Adapters own coordinate derivation, source text
normalization and revision mapping.

For `中😀\r\n文`, the emoji has `Start = 1`, `Length = 2`, `Line = 1`,
`Column = 2`. The final character starts at offset `5`, line `2`, column `1`.
The core stores coordinates; it does not parse or recalculate them.

## Reports and operation boundaries

`OccurrenceId` identifies one concrete occurrence, not a diagnostic kind.
The producer or observation boundary supplies it; a shared sink requires IDs
unique across all its producers and scopes. Forwarding the same event retains
the same ID, while repeated executions use distinct IDs even when code,
message and source match. Correlation does not replace occurrence identity.

`OperationId`, `CorrelationId`, `ScopeId`, nonnegative `Generation` and
`ObservedAt` are optional host metadata. The core generates neither IDs nor
timestamps. `Context` contains copied string pairs under host-owned documented
keys. Observation metadata does not belong in deterministic state or hashes.

`IDiagnosticSink.Report` offers synchronous lightweight observation. It does
not acknowledge persistence or define operation outcome. Hosts isolate sink
exceptions at an explicit publication boundary and may retain technical
details in a separate local store. Validation results may continue to be
returned as collections without injecting a sink into every validator.

## Bounded collection

`CollectingDiagnosticSink` retains the newest `Capacity` accepted reports in
insertion order. Capacity is positive and defaults to `256`. When full, a new
occurrence evicts the oldest retained report. `Reports` returns an immutable
snapshot, so later delivery does not mutate an earlier observation.

Deduplication uses ordinal `OccurrenceId` equality within the retained window.
Repeated delivery of an ID in that window is rejected without changing the
original report. A new ID is accepted regardless of code, message or location.
Eviction also forgets that occurrence's ID, bounding both payload and identity
storage. A delayed duplicate of an evicted event can therefore be accepted
again. Hosts requiring deduplication across a longer lifetime must supply an
explicit bounded lifetime policy at their publication boundary.

`Report` silently ignores a duplicate; `TryReport` returns `false` for it.
`AcceptedCount` counts all accepted reports, including subsequently evicted
ones. `DuplicateCount` counts rejected deliveries. `DroppedCount` counts
evictions. `Count` is the current retained count. These counters are cumulative
for the sink lifetime. Calls and individual property reads are thread-safe;
multiple separate property reads are not an atomic statistics snapshot.

No severity causes the collector to throw. A null report is invalid caller
input. The collector is an event history, not a version-aware static analysis
store; controllers retain responsibility for rejecting stale snapshots and
replacing or clearing analysis results.

## Serialization boundary

Core classes are internal application contracts, not a versioned wire schema.
Hosts use explicit DTOs and mappers to publish producer, code, severity,
message, location and documented properties. Wire severity uses a documented
string representation rather than relying on CLR enum ordinals. Hosts own
schema versions, omitted/unknown field behavior and output filtering. Local
exception details and platform objects must remain outside the wire DTO.
