using System;

namespace Module.Verification.StateSnapshot
{
    public readonly struct StateSnapshotReference : IEquatable<StateSnapshotReference>
    {
        public StateSnapshotReference(Guid channelId, long captureId)
        {
            if (channelId == Guid.Empty) throw new ArgumentException("A channel identity is required.", nameof(channelId));
            if (captureId < 1) throw new ArgumentOutOfRangeException(nameof(captureId));
            ChannelId = channelId;
            CaptureId = captureId;
        }

        public Guid ChannelId { get; }
        public long CaptureId { get; }

        public bool Equals(StateSnapshotReference other)
        {
            return ChannelId == other.ChannelId && CaptureId == other.CaptureId;
        }

        public override bool Equals(object obj)
        {
            return obj is StateSnapshotReference && Equals((StateSnapshotReference)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChannelId, CaptureId);
        }

        public static bool operator ==(StateSnapshotReference left, StateSnapshotReference right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StateSnapshotReference left, StateSnapshotReference right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class StateSnapshotCaptureFailure
    {
        public StateSnapshotCaptureFailure(string code, string detail)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A failure code is required.", nameof(code));
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public string Code { get; }
        public string Detail { get; }
    }

    public enum StateSnapshotReadState
    {
        Empty,
        Available,
        CaptureFailed,
        Evicted,
        ForeignReference
    }

    public sealed class StateSnapshotRead<TSnapshot>
    {
        internal StateSnapshotRead(StateSnapshotReadState state, StateSnapshotReference reference, TSnapshot snapshot, StateSnapshotCaptureFailure failure)
        {
            State = state;
            Reference = reference;
            Snapshot = snapshot;
            Failure = failure;
        }

        public StateSnapshotReadState State { get; }
        public StateSnapshotReference Reference { get; }
        public TSnapshot Snapshot { get; }
        public StateSnapshotCaptureFailure Failure { get; }
        public bool HasSnapshot
        {
            get { return State == StateSnapshotReadState.Available; }
        }
    }

    public interface IStateSnapshotReader<TSnapshot>
    {
        StateSnapshotRead<TSnapshot> ReadLatest();
        StateSnapshotRead<TSnapshot> Read(StateSnapshotReference reference);
    }

    public interface IStateSnapshotPublisher<TSnapshot>
    {
        StateSnapshotReference Publish(TSnapshot snapshot);
        StateSnapshotReference ReportStateSnapshotCaptureFailure(StateSnapshotCaptureFailure failure);
    }
}
