#nullable enable

namespace Module.Verification.Diagnostics
{
    /// <summary>Source data only; absent coordinates remain null and display paths are not identities.</summary>
    public sealed class DiagnosticLocation
    {
        public DiagnosticLocation(string? resourceId = null, string? resourceRevision = null, DiagnosticTextSpan? textSpan = null, string? fieldPath = null, string? displayPath = null)
        {
            ResourceId = resourceId;
            ResourceRevision = resourceRevision;
            TextSpan = textSpan;
            FieldPath = fieldPath;
            DisplayPath = displayPath;
        }

        public string? ResourceId { get; }
        public string? ResourceRevision { get; }
        public DiagnosticTextSpan? TextSpan { get; }
        public string? FieldPath { get; }
        public string? DisplayPath { get; }
    }
}
