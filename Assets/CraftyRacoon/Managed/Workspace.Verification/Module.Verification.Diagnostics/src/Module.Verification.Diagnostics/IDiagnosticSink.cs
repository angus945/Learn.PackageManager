namespace Module.Verification.Diagnostics
{
    /// <summary>Observes a report without defining execution, cancellation or durable delivery semantics.</summary>
    public interface IDiagnosticSink
    {
        void Report(DiagnosticReport report);
    }
}
