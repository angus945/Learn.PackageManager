using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Module.Verification.Evidence
{
    public sealed class EvidenceManifest
    {
        public EvidenceManifest(string runId, string caseId, string buildId = null, string fixtureId = null)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A run identity is required.", nameof(runId));
            if (string.IsNullOrWhiteSpace(caseId)) throw new ArgumentException("A case identity is required.", nameof(caseId));
            RunId = runId;
            CaseId = caseId;
            BuildId = buildId;
            FixtureId = fixtureId;
        }

        public string RunId { get; }
        public string CaseId { get; }
        public string BuildId { get; }
        public string FixtureId { get; }
    }

    public readonly struct EvidenceReference : IEquatable<EvidenceReference>
    {
        public EvidenceReference(string scheme, string value)
        {
            if (string.IsNullOrWhiteSpace(scheme)) throw new ArgumentException("A reference scheme is required.", nameof(scheme));
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A reference value is required.", nameof(value));
            Scheme = scheme;
            Value = value;
        }

        public string Scheme { get; }
        public string Value { get; }
        public bool IsValid
        {
            get { return !string.IsNullOrWhiteSpace(Scheme) && !string.IsNullOrWhiteSpace(Value); }
        }

        public bool Equals(EvidenceReference other)
        {
            return string.Equals(Scheme, other.Scheme, StringComparison.Ordinal) && string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is EvidenceReference && Equals((EvidenceReference)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Scheme), StringComparer.Ordinal.GetHashCode(Value));
        }

        public static bool operator ==(EvidenceReference left, EvidenceReference right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EvidenceReference left, EvidenceReference right)
        {
            return !left.Equals(right);
        }
    }

    public enum EvidenceKind
    {
        FactStream,
        StateSnapshot,
        Evaluation,
        Trace,
        Diagnostic,
        Attachment,
        ReplayRecording
    }

    public sealed class EvidenceEntry
    {
        public EvidenceEntry(EvidenceKind kind, string label, EvidenceReference reference, long estimatedBytes, bool truncated = false, bool redacted = false, IReadOnlyDictionary<string, string> metadata = null)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("An evidence label is required.", nameof(label));
            if (!reference.IsValid) throw new ArgumentException("A valid evidence reference is required.", nameof(reference));
            if (estimatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedBytes));
            Kind = kind;
            Label = label;
            Reference = reference;
            EstimatedBytes = estimatedBytes;
            Truncated = truncated;
            Redacted = redacted;
            Dictionary<string, string> copiedMetadata = metadata == null ? new Dictionary<string, string>() : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
            Metadata = new ReadOnlyDictionary<string, string>(copiedMetadata);
        }

        public EvidenceKind Kind { get; }
        public string Label { get; }
        public EvidenceReference Reference { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }
        public long EstimatedBytes { get; }
        public bool Truncated { get; }
        public bool Redacted { get; }
    }

    public sealed class EvidenceBundle
    {
        public EvidenceBundle(EvidenceManifest manifest, IEnumerable<EvidenceEntry> entries, long retainedBytes, int droppedEntryCount, string firstFailure, IEnumerable<string> cleanupErrors)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Entries = new List<EvidenceEntry>(entries ?? throw new ArgumentNullException(nameof(entries))).AsReadOnly();
            if (retainedBytes < 0) throw new ArgumentOutOfRangeException(nameof(retainedBytes));
            if (droppedEntryCount < 0) throw new ArgumentOutOfRangeException(nameof(droppedEntryCount));
            RetainedBytes = retainedBytes;
            DroppedEntryCount = droppedEntryCount;
            FirstFailure = firstFailure;
            CleanupErrors = new List<string>(cleanupErrors ?? throw new ArgumentNullException(nameof(cleanupErrors))).AsReadOnly();
        }

        public EvidenceManifest Manifest { get; }
        public IReadOnlyList<EvidenceEntry> Entries { get; }
        public long RetainedBytes { get; }
        public int DroppedEntryCount { get; }
        public string FirstFailure { get; }
        public IReadOnlyList<string> CleanupErrors { get; }
    }

    public sealed class EvidenceWriteResult
    {
        public EvidenceWriteResult(bool written, EvidenceReference reference, string code)
        {
            Written = written;
            Reference = reference;
            Code = code ?? string.Empty;
        }

        public bool Written { get; }
        public EvidenceReference Reference { get; }
        public string Code { get; }
    }

    public interface IEvidenceSink
    {
        EvidenceWriteResult Write(EvidenceBundle bundle);
    }
}
