using System;
using System.Collections.Generic;

namespace Module.Verification.StateSnapshot
{
    public sealed class StateSnapshotChannel<TSnapshot>
    {
        private sealed class Entry
        {
            public Entry(StateSnapshotReference reference, TSnapshot snapshot, StateSnapshotCaptureFailure failure)
            {
                Reference = reference;
                Snapshot = snapshot;
                Failure = failure;
            }

            public StateSnapshotReference Reference { get; }
            public TSnapshot Snapshot { get; }
            public StateSnapshotCaptureFailure Failure { get; }
        }

        private sealed class Reader : IStateSnapshotReader<TSnapshot>
        {
            private readonly StateSnapshotChannel<TSnapshot> owner;

            public Reader(StateSnapshotChannel<TSnapshot> owner)
            {
                this.owner = owner;
            }

            public StateSnapshotRead<TSnapshot> ReadLatest()
            {
                return owner.ReadLatest();
            }

            public StateSnapshotRead<TSnapshot> Read(StateSnapshotReference reference)
            {
                return owner.Read(reference);
            }
        }

        private sealed class Publisher : IStateSnapshotPublisher<TSnapshot>
        {
            private readonly StateSnapshotChannel<TSnapshot> owner;

            public Publisher(StateSnapshotChannel<TSnapshot> owner)
            {
                this.owner = owner;
            }

            public StateSnapshotReference Publish(TSnapshot snapshot)
            {
                return owner.Publish(snapshot);
            }

            public StateSnapshotReference ReportStateSnapshotCaptureFailure(StateSnapshotCaptureFailure failure)
            {
                return owner.ReportStateSnapshotCaptureFailure(failure);
            }
        }

        private readonly object gate = new object();
        private readonly Guid channelId = Guid.NewGuid();
        private readonly Queue<Entry> retained = new Queue<Entry>();
        private readonly Dictionary<long, Entry> byCaptureId = new Dictionary<long, Entry>();
        private long nextCaptureId;

        public StateSnapshotChannel(int capacity = 64)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            ReaderPort = new Reader(this);
            PublisherPort = new Publisher(this);
        }

        public int Capacity { get; }
        public IStateSnapshotReader<TSnapshot> ReaderPort { get; }
        public IStateSnapshotPublisher<TSnapshot> PublisherPort { get; }

        private StateSnapshotReference Publish(TSnapshot snapshot)
        {
            if (ReferenceEquals(snapshot, null)) throw new ArgumentNullException(nameof(snapshot));
            lock (gate)
            {
                StateSnapshotReference reference = NextReference();
                Retain(new Entry(reference, snapshot, null));
                return reference;
            }
        }

        private StateSnapshotReference ReportStateSnapshotCaptureFailure(StateSnapshotCaptureFailure failure)
        {
            if (failure == null) throw new ArgumentNullException(nameof(failure));
            lock (gate)
            {
                StateSnapshotReference reference = NextReference();
                Retain(new Entry(reference, default(TSnapshot), failure));
                return reference;
            }
        }

        private StateSnapshotReference NextReference()
        {
            nextCaptureId = checked(nextCaptureId + 1);
            return new StateSnapshotReference(channelId, nextCaptureId);
        }

        private void Retain(Entry entry)
        {
            if (retained.Count == Capacity)
            {
                Entry removed = retained.Dequeue();
                byCaptureId.Remove(removed.Reference.CaptureId);
            }
            retained.Enqueue(entry);
            byCaptureId.Add(entry.Reference.CaptureId, entry);
        }

        private StateSnapshotRead<TSnapshot> ReadLatest()
        {
            lock (gate)
            {
                if (retained.Count == 0) return Empty(StateSnapshotReadState.Empty);
                Entry[] entries = retained.ToArray();
                return ToRead(entries[entries.Length - 1]);
            }
        }

        private StateSnapshotRead<TSnapshot> Read(StateSnapshotReference reference)
        {
            lock (gate)
            {
                if (reference.ChannelId != channelId) return Empty(StateSnapshotReadState.ForeignReference);
                Entry entry;
                if (byCaptureId.TryGetValue(reference.CaptureId, out entry)) return ToRead(entry);
                if (reference.CaptureId <= nextCaptureId) return Empty(StateSnapshotReadState.Evicted);
                return Empty(StateSnapshotReadState.Empty);
            }
        }

        private static StateSnapshotRead<TSnapshot> ToRead(Entry entry)
        {
            StateSnapshotReadState state = entry.Failure == null ? StateSnapshotReadState.Available : StateSnapshotReadState.CaptureFailed;
            return new StateSnapshotRead<TSnapshot>(state, entry.Reference, entry.Snapshot, entry.Failure);
        }

        private static StateSnapshotRead<TSnapshot> Empty(StateSnapshotReadState state)
        {
            return new StateSnapshotRead<TSnapshot>(state, default(StateSnapshotReference), default(TSnapshot), null);
        }
    }
}
