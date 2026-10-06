using System;
using Xunit;

namespace Module.Verification.TraceBuffer.Tests
{
    public sealed class TraceBufferTests
    {
        [Fact]
        public void PagesAdvanceExclusiveCursorWithoutDuplicates()
        {
            TraceBuffer<string> buffer = new TraceBuffer<string>(4);
            buffer.Writer.Append("a"); buffer.Writer.Append("b"); buffer.Writer.Append("c");
            TraceRead<string> first = buffer.Reader.Read(default, 2);
            Assert.Equal("a", first.Items[0]);
            Assert.Equal("b", first.Items[1]);
            Assert.Equal(TraceReadState.Available, first.State);
            Assert.True(first.HasMore);
            TraceRead<string> second = buffer.Reader.Read(first.NextCursor, 2);
            Assert.Single(second.Items);
            Assert.Equal("c", second.Items[0]);
            Assert.False(second.HasMore);
            Assert.Empty(buffer.Reader.Read(second.NextCursor, 2).Items);
        }

        [Fact]
        public void SlowReaderReceivesExactGapAndOnlyRetainedRecords()
        {
            TraceBuffer<int> buffer = new TraceBuffer<int>(2);
            buffer.Writer.Append(1);
            TraceCursor cursor = buffer.Reader.Read(default, 1).NextCursor;
            for (int i = 2; i <= 5; i++) buffer.Writer.Append(i);
            TraceRead<int> batch = buffer.Reader.Read(cursor, 100);
            Assert.Equal(2, batch.MissedCount);
            Assert.Equal(3, batch.OverwrittenCount);
            Assert.Equal(4, batch.Items[0]);
            Assert.Equal(5, batch.Items[1]);
            Assert.Equal(TraceReadState.Evicted, batch.State);
            Assert.Equal(0, buffer.Reader.Read(batch.NextCursor, 2).MissedCount);
            Assert.Equal(3, buffer.Reader.Read(default, 2).MissedCount);
        }

        [Fact]
        public void NewStreamDetectsForeignCursorAndBeginsOwnHistory()
        {
            TraceBuffer<int> old = new TraceBuffer<int>(2);
            old.Writer.Append(1); old.Writer.Append(2);
            TraceCursor cursor = old.Reader.Read(default, 2).NextCursor;
            TraceBuffer<int> next = new TraceBuffer<int>(2);
            next.Writer.Append(9);
            TraceRead<int> batch = next.Reader.Read(cursor, 2);
            Assert.Equal(TraceReadState.ForeignCursor, batch.State);
            Assert.Equal(9, batch.Items[0]);
            Assert.NotEqual(cursor.StreamId, batch.NextCursor.StreamId);
        }

        [Fact]
        public void SnapshotSurvivesOverwriteAndReadersHaveIndependentPositions()
        {
            TraceBuffer<int> buffer = new TraceBuffer<int>(1);
            buffer.Writer.Append(10);
            TraceRead<int> first = buffer.Reader.Read(default, 1);
            buffer.Writer.Append(20);
            Assert.Equal(10, first.Items[0]);
            Assert.Equal(20, buffer.Reader.Read(first.NextCursor, 1).Items[0]);
            Assert.Equal(20, buffer.Reader.Read(default, 1).Items[0]);
        }

        [Fact]
        public void ReaderCannotBeCastToWriterOrOwner()
        {
            TraceBuffer<string> buffer = new TraceBuffer<string>(1);
            Assert.IsNotAssignableFrom<ITraceWriter<string>>(buffer.Reader);
            Assert.IsNotAssignableFrom<TraceBuffer<string>>(buffer.Reader);
            Assert.IsNotAssignableFrom<ITraceReader<string>>(buffer.Writer);
        }

        [Fact]
        public void InvalidReadsDoNotConsumeAnything()
        {
            TraceBuffer<string> buffer = new TraceBuffer<string>(2);
            buffer.Writer.Append("a");
            TraceCursor cursor = buffer.Reader.Read(default, 1).NextCursor;
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Reader.Read(cursor, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Reader.Read(new TraceCursor(cursor.StreamId, 99), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TraceCursor(Guid.Empty, 1));
            Assert.Throws<ArgumentNullException>(() => buffer.Writer.Append(null));
            Assert.Single(buffer.Reader.Read(default, 2).Items);
        }

        [Fact]
        public void EmptyStreamHasStableCursorAndNoFalseGap()
        {
            TraceBuffer<int> buffer = new TraceBuffer<int>(1);
            TraceRead<int> empty = buffer.Reader.Read(default, 1);
            Assert.Empty(empty.Items);
            Assert.Equal(0, empty.MissedCount);
            Assert.Equal(0, empty.NextCursor.AfterSequence);
            Assert.Equal(TraceReadState.End, empty.State);
            buffer.Writer.Append(5);
            Assert.Equal(5, buffer.Reader.Read(empty.NextCursor, 1).Items[0]);
        }
    }
}
