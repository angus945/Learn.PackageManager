using System;

namespace Module.Verification.SystemFact.Observability
{
    public sealed class FactObservationContext
    {
        public FactObservationContext(long sequence, DateTimeOffset timestamp)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            Timestamp = timestamp;
        }

        public long Sequence { get; }
        public DateTimeOffset Timestamp { get; }
    }
}
