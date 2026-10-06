using System;
using System.Collections.Generic;

namespace Module.Verification.TraceBuffer
{
    public readonly struct TraceCursor
    {
        public TraceCursor(Guid streamId, long afterSequence)
        {
            if (afterSequence < 0 || (streamId == Guid.Empty && afterSequence != 0)) throw new ArgumentOutOfRangeException(nameof(afterSequence));
            StreamId = streamId;
            AfterSequence = afterSequence;
        }

        public Guid StreamId { get; }
        public long AfterSequence { get; }
    }

    public enum TraceReadState
    {
        Available,
        End,
        Evicted,
        ForeignCursor
    }

    public sealed class TraceRead<T>
    {
        internal TraceRead(TraceReadState state, T[] items, TraceCursor nextCursor, long missedCount, long overwrittenCount, bool hasMore)
        {
            State = state;
            Items = Array.AsReadOnly(items);
            NextCursor = nextCursor;
            MissedCount = missedCount;
            OverwrittenCount = overwrittenCount;
            HasMore = hasMore;
        }

        public TraceReadState State { get; }
        public IReadOnlyList<T> Items { get; }
        public TraceCursor NextCursor { get; }
        public long MissedCount { get; }
        public long OverwrittenCount { get; }
        public bool HasMore { get; }
    }
}
