#nullable enable

using System;
using System.Collections.Generic;

namespace Module.Verification.Diagnostics
{
    /// <summary>An immutable problem description. Severity does not decide operation outcome.</summary>
    public sealed class Diagnostic
    {
        public Diagnostic(DiagnosticDescriptor descriptor, string message, DiagnosticSeverity? severity = null, DiagnosticLocation? location = null, IReadOnlyDictionary<string, string>? properties = null, IReadOnlyList<DiagnosticLocation>? relatedLocations = null, IReadOnlyList<string>? messageArguments = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Message = message ?? throw new ArgumentNullException(nameof(message));
            DiagnosticSeverity actualSeverity = severity ?? descriptor.DefaultSeverity;
            DiagnosticCollections.ValidateSeverity(actualSeverity, nameof(severity));
            Severity = actualSeverity;
            Location = location;
            Properties = DiagnosticCollections.CopyDictionary(properties, nameof(properties));
            RelatedLocations = DiagnosticCollections.CopyList(relatedLocations, nameof(relatedLocations));
            MessageArguments = DiagnosticCollections.CopyList(messageArguments, nameof(messageArguments));
        }

        public DiagnosticDescriptor Descriptor { get; }
        public string Producer
        {
            get
            {
                return Descriptor.Producer;
            }
        }

        public string Code
        {
            get
            {
                return Descriptor.Code;
            }
        }

        public string Message { get; }
        public DiagnosticSeverity Severity { get; }
        public DiagnosticLocation? Location { get; }
        public IReadOnlyDictionary<string, string> Properties { get; }
        public IReadOnlyList<DiagnosticLocation> RelatedLocations { get; }
        public IReadOnlyList<string> MessageArguments { get; }

        public static Diagnostic Create(string producer, string code, DiagnosticSeverity severity, string message, DiagnosticLocation? location = null, IReadOnlyDictionary<string, string>? properties = null)
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor(producer, code, severity);
            return new Diagnostic(descriptor, message, severity, location, properties);
        }

        public static Diagnostic Error(string producer, string code, string message, DiagnosticLocation? location = null, IReadOnlyDictionary<string, string>? properties = null)
        {
            return Create(producer, code, DiagnosticSeverity.Error, message, location, properties);
        }

        public static Diagnostic Warning(string producer, string code, string message, DiagnosticLocation? location = null, IReadOnlyDictionary<string, string>? properties = null)
        {
            return Create(producer, code, DiagnosticSeverity.Warning, message, location, properties);
        }

        public static Diagnostic Info(string producer, string code, string message, DiagnosticLocation? location = null, IReadOnlyDictionary<string, string>? properties = null)
        {
            return Create(producer, code, DiagnosticSeverity.Info, message, location, properties);
        }

        public Diagnostic WithLocation(DiagnosticLocation? location)
        {
            return new Diagnostic(Descriptor, Message, Severity, location, Properties, RelatedLocations, MessageArguments);
        }

        public Diagnostic WithProperties(IReadOnlyDictionary<string, string> properties)
        {
            return new Diagnostic(Descriptor, Message, Severity, Location, properties, RelatedLocations, MessageArguments);
        }

        public Diagnostic WithResource(string? resourceId, string? resourceRevision, string? displayPath = null)
        {
            DiagnosticLocation location = new DiagnosticLocation(resourceId, resourceRevision, Location?.TextSpan, Location?.FieldPath, displayPath ?? Location?.DisplayPath);
            return WithLocation(location);
        }
    }
}
