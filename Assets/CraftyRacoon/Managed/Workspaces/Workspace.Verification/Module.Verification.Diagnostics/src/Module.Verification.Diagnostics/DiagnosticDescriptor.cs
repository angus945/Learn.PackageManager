#nullable enable

using System;

namespace Module.Verification.Diagnostics
{
    /// <summary>Defines a producer-owned diagnostic kind. Identity is the ordinal producer and code pair.</summary>
    public sealed class DiagnosticDescriptor : IEquatable<DiagnosticDescriptor>
    {
        public DiagnosticDescriptor(string producer, string code, DiagnosticSeverity defaultSeverity, string? category = null, string? defaultMessageTemplate = null, string? messageKey = null)
        {
            if (string.IsNullOrWhiteSpace(producer))
            {
                throw new ArgumentException("A stable producer identifier is required.", nameof(producer));
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("A stable diagnostic code is required.", nameof(code));
            }

            DiagnosticCollections.ValidateSeverity(defaultSeverity, nameof(defaultSeverity));
            Producer = producer;
            Code = code;
            DefaultSeverity = defaultSeverity;
            Category = category;
            DefaultMessageTemplate = defaultMessageTemplate;
            MessageKey = messageKey;
        }

        public string Producer { get; }
        public string Code { get; }
        public DiagnosticSeverity DefaultSeverity { get; }
        public string? Category { get; }
        public string? DefaultMessageTemplate { get; }
        public string? MessageKey { get; }

        public bool Equals(DiagnosticDescriptor? other)
        {
            if (other == null)
            {
                return false;
            }

            bool sameProducer = string.Equals(Producer, other.Producer, StringComparison.Ordinal);
            bool sameCode = string.Equals(Code, other.Code, StringComparison.Ordinal);
            return sameProducer && sameCode;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as DiagnosticDescriptor);
        }

        public override int GetHashCode()
        {
            int producerHash = StringComparer.Ordinal.GetHashCode(Producer);
            int codeHash = StringComparer.Ordinal.GetHashCode(Code);
            return HashCode.Combine(producerHash, codeHash);
        }
    }
}
