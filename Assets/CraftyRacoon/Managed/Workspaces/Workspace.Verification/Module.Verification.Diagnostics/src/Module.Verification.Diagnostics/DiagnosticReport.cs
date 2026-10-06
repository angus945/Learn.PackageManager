#nullable enable

using System;
using System.Collections.Generic;

namespace Module.Verification.Diagnostics
{
    /// <summary>One observation of a problem. Host observation data must remain outside deterministic state.</summary>
    public sealed class DiagnosticReport
    {
        public DiagnosticReport(Diagnostic diagnostic, string occurrenceId, string? operationId = null, string? correlationId = null, string? scopeId = null, long? generation = null, DateTimeOffset? observedAt = null, IReadOnlyDictionary<string, string>? context = null)
        {
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
            if (string.IsNullOrWhiteSpace(occurrenceId))
            {
                throw new ArgumentException("An occurrence identifier is required.", nameof(occurrenceId));
            }

            if (generation.HasValue && generation.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            OccurrenceId = occurrenceId;
            OperationId = operationId;
            CorrelationId = correlationId;
            ScopeId = scopeId;
            Generation = generation;
            ObservedAt = observedAt;
            Context = DiagnosticCollections.CopyDictionary(context, nameof(context));
        }

        public Diagnostic Diagnostic { get; }
        public string OccurrenceId { get; }
        public string? OperationId { get; }
        public string? CorrelationId { get; }
        public string? ScopeId { get; }
        public long? Generation { get; }
        public DateTimeOffset? ObservedAt { get; }
        public IReadOnlyDictionary<string, string> Context { get; }
    }
}
