# module.verification.diagnostics

Immutable, source-neutral diagnostic descriptions and an observation sink
contract. This standalone source module has no Narrative, Editor, Unity,
external-package or platform I/O dependency.

The library targets `netstandard2.1` with C# 9; tests target `net10.0`.
This matches the existing neutral deterministic modules. The target permits
compatible hosts to consume the assembly; it does not establish tested support
for a particular Unity version or source compiler.

## Responsibilities

- `DiagnosticDescriptor` defines a producer-owned identity and default metadata.
- `Diagnostic` preserves actual severity, fallback message, source locations,
  structured string properties and optional ordered message arguments.
- `DiagnosticReport` adds one occurrence and optional host observation context.
- `IDiagnosticSink` observes a report without deciding execution outcome.
- `CollectingDiagnosticSink` keeps a bounded, thread-safe in-memory history.

Language rules, execution state, exception logging, static analysis snapshots,
UI navigation, wire serialization and delivery failure policy belong to the
owning producer or host. This module does not create a global service or clock.

Language, Flow, Runtime, assets, schema, settings and templates create this model
directly through producer-owned factories. There is no migration adapter layer.
Editor Application owns validation snapshots and Preview event history.
`WorkspaceDocumentOperationResult.Diagnostic` provides failure content for
document operations while preserving existing status and code values;
successful, unchanged and committed results have no failure diagnostic.
Control publishes explicit DTO projections at the Web boundary.

## Use

```csharp
using Module.Verification.Diagnostics;

DiagnosticDescriptor descriptor = new DiagnosticDescriptor("sample.validator", "missing.parameter", DiagnosticSeverity.Error);
DiagnosticTextSpan span = new DiagnosticTextSpan(8, 5, 1, 9);
DiagnosticLocation location = new DiagnosticLocation("script:1", "revision:3", span, "parameters.actor");
Diagnostic diagnostic = new Diagnostic(descriptor, "Actor is required.", location: location);
DiagnosticReport report = new DiagnosticReport(diagnostic, "operation:4/occurrence:1", operationId: "operation:4");
CollectingDiagnosticSink sink = new CollectingDiagnosticSink(256);
sink.Report(report);
```

The producer supplies stable IDs. The operation owner commits its result before
the host observation boundary publishes the report. Host adapters must isolate
sink failures so output behavior cannot replace that result.

See [contracts.md](docs/contracts.md) for identity, coordinates, immutability,
retention, observation and serialization rules.

## Verification

From the NarrativeEditor root:

```powershell
dotnet test modules/module.verification.diagnostics/tests/Module.Verification.Diagnostics.Tests/Module.Verification.Diagnostics.Tests.csproj -c Release
```

From this directory:

```powershell
dotnet test tests/Module.Verification.Diagnostics.Tests/Module.Verification.Diagnostics.Tests.csproj -c Release
```

The focused tests cover neutral assembly dependencies, immutable collections,
producer/code identity, optional coordinates, UTF-16 examples, concurrent
delivery, bounded retention and duplicate versus distinct occurrences.
