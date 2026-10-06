using Xunit;

namespace Module.Verification.StateSnapshot.Tests
{
    public sealed class StateSnapshotChannelTests
    {
        private sealed class Snapshot
        {
            public Snapshot(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        [Fact]
        public void PublishReturnsStableReferenceAndReadSpecificReturnsMatchingSnapshot()
        {
            StateSnapshotChannel<Snapshot> channel = new StateSnapshotChannel<Snapshot>();
            Snapshot snapshot = new Snapshot(7);
            StateSnapshotReference reference = channel.PublisherPort.Publish(snapshot);

            StateSnapshotRead<Snapshot> first = channel.ReaderPort.Read(reference);
            StateSnapshotRead<Snapshot> second = channel.ReaderPort.Read(reference);

            Assert.Equal(reference, first.Reference);
            Assert.Equal(reference, second.Reference);
            Assert.Same(snapshot, first.Snapshot);
        }

        [Fact]
        public void ReadLatestReturnsNewestSnapshot()
        {
            StateSnapshotChannel<Snapshot> channel = new StateSnapshotChannel<Snapshot>();
            channel.PublisherPort.Publish(new Snapshot(1));
            channel.PublisherPort.Publish(new Snapshot(2));

            Assert.Equal(2, channel.ReaderPort.ReadLatest().Snapshot.Value);
        }

        [Fact]
        public void EvictedAndForeignReferencesReturnExplicitStates()
        {
            StateSnapshotChannel<Snapshot> channel = new StateSnapshotChannel<Snapshot>(1);
            StateSnapshotReference evicted = channel.PublisherPort.Publish(new Snapshot(1));
            channel.PublisherPort.Publish(new Snapshot(2));
            StateSnapshotChannel<Snapshot> other = new StateSnapshotChannel<Snapshot>();
            StateSnapshotReference foreign = other.PublisherPort.Publish(new Snapshot(3));

            Assert.Equal(StateSnapshotReadState.Evicted, channel.ReaderPort.Read(evicted).State);
            Assert.Equal(StateSnapshotReadState.ForeignReference, channel.ReaderPort.Read(foreign).State);
        }

        [Fact]
        public void CaptureFailureIsRepresentedExplicitly()
        {
            StateSnapshotChannel<Snapshot> channel = new StateSnapshotChannel<Snapshot>();
            StateSnapshotCaptureFailure failure = new StateSnapshotCaptureFailure("capture.failed", "expected");
            StateSnapshotReference reference = channel.PublisherPort.ReportStateSnapshotCaptureFailure(failure);
            StateSnapshotRead<Snapshot> read = channel.ReaderPort.Read(reference);

            Assert.Equal(StateSnapshotReadState.CaptureFailed, read.State);
            Assert.Same(failure, read.Failure);
            Assert.False(read.HasSnapshot);
        }

        [Fact]
        public void SnapshotPayloadIsNotMutatedByChannel()
        {
            StateSnapshotChannel<Snapshot> channel = new StateSnapshotChannel<Snapshot>();
            Snapshot snapshot = new Snapshot(11);
            StateSnapshotReference reference = channel.PublisherPort.Publish(snapshot);

            Assert.Same(snapshot, channel.ReaderPort.Read(reference).Snapshot);
            Assert.Equal(11, snapshot.Value);
        }
    }
}
