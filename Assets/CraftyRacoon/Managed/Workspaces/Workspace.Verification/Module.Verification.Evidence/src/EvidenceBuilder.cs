using System;
using System.Collections.Generic;

namespace Module.Verification.Evidence
{
    public sealed class EvidenceBuilder
    {
        private readonly EvidenceManifest manifest;
        private readonly long maxBytes;
        private readonly int maxEntries;
        private readonly List<EvidenceEntry> entries = new List<EvidenceEntry>();
        private readonly List<string> cleanupErrors = new List<string>();
        private long retainedBytes;
        private int droppedEntryCount;
        private string firstFailure;

        public EvidenceBuilder(EvidenceManifest manifest, long maxBytes = 4194304, int maxEntries = 1024)
        {
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            if (maxBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            this.maxBytes = maxBytes;
            this.maxEntries = maxEntries;
        }

        public bool TryAdd(EvidenceEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entries.Count >= maxEntries || entry.EstimatedBytes > maxBytes - retainedBytes)
            {
                droppedEntryCount++;
                return false;
            }
            entries.Add(entry);
            retainedBytes += entry.EstimatedBytes;
            return true;
        }

        public void RecordFailure(string failure)
        {
            if (firstFailure == null) firstFailure = failure ?? string.Empty;
        }

        public void RecordCleanupError(string error)
        {
            cleanupErrors.Add(error ?? string.Empty);
        }

        public EvidenceBundle Build()
        {
            return new EvidenceBundle(manifest, entries, retainedBytes, droppedEntryCount, firstFailure, cleanupErrors);
        }
    }
}
