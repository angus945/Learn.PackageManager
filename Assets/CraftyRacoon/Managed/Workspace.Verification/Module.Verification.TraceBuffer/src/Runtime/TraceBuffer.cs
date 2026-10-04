using System;

namespace Module.Verification.TraceBuffer
{
    /// <summary>Single-threaded bounded temporal storage. Payload T must be immutable; reads do not deep-clone T.</summary>
    public sealed class TraceBuffer<T> : ITraceBuffer<T>
    {
        private readonly T[] items;
        private readonly Guid streamId = Guid.NewGuid();
        private int next;
        private int count;
        private long lastSequence;

        public TraceBuffer(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            items = new T[capacity];
            Reader = new ReaderPort(this);
            Writer = new WriterPort(this);
        }

        public ITraceReader<T> Reader { get; }
        public ITraceWriter<T> Writer { get; }
        public int Capacity
        {
            get { return items.Length; }
        }
        public long OverwrittenCount
        {
            get { return lastSequence - count; }
        }

        public void Append(T item)
        {
            if (ReferenceEquals(item, null)) throw new ArgumentNullException(nameof(item));
            if (lastSequence == long.MaxValue) throw new InvalidOperationException("Trace sequence exhausted.");
            lastSequence++;
            items[next] = item;
            next = (next + 1) % items.Length;
            if (count < items.Length) count++;
        }

        public TraceRead<T> Read(TraceCursor cursor, int maxItems)
        {
            if (maxItems < 1) throw new ArgumentOutOfRangeException(nameof(maxItems));
            bool foreign = cursor.StreamId != Guid.Empty && cursor.StreamId != streamId;
            long after = foreign ? 0 : cursor.AfterSequence;
            if (after > lastSequence) throw new ArgumentOutOfRangeException(nameof(cursor), "Cursor is ahead of this stream.");
            long overwritten = lastSequence - count;
            long missed = Math.Max(0, overwritten - after);
            long effectiveAfter = Math.Max(after, overwritten);
            int take = (int)Math.Min(maxItems, lastSequence - effectiveAfter);
            T[] copy = new T[take];
            int oldestIndex = (next - count + items.Length) % items.Length;
            for (int index = 0; index < take; index++)
            {
                int offset = (int)(effectiveAfter - overwritten) + index;
                copy[index] = items[(oldestIndex + offset) % items.Length];
            }
            long consumed = effectiveAfter + take;
            TraceReadState state = SelectState(foreign, missed, take);
            return new TraceRead<T>(state, copy, new TraceCursor(streamId, consumed), missed, overwritten, consumed < lastSequence);
        }

        private static TraceReadState SelectState(bool foreign, long missed, int count)
        {
            if (foreign) return TraceReadState.ForeignCursor;
            if (missed > 0) return TraceReadState.Evicted;
            return count == 0 ? TraceReadState.End : TraceReadState.Available;
        }

        private sealed class ReaderPort : ITraceReader<T>
        {
            private readonly TraceBuffer<T> owner;

            internal ReaderPort(TraceBuffer<T> owner)
            {
                this.owner = owner;
            }

            public TraceRead<T> Read(TraceCursor cursor, int maxItems)
            {
                return owner.Read(cursor, maxItems);
            }
        }

        private sealed class WriterPort : ITraceWriter<T>
        {
            private readonly TraceBuffer<T> owner;

            internal WriterPort(TraceBuffer<T> owner)
            {
                this.owner = owner;
            }

            public void Append(T item)
            {
                owner.Append(item);
            }
        }
    }
}
