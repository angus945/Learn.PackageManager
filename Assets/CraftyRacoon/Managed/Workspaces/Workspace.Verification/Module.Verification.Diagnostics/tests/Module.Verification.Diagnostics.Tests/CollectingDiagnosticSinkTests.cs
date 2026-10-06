using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Module.Verification.Diagnostics.Tests
{
    public sealed class CollectingDiagnosticSinkTests
    {
        [Fact]
        public void RepeatedDeliveriesAreRejectedWithoutMergingDifferentOccurrences()
        {
            CollectingDiagnosticSink sink = new CollectingDiagnosticSink(3);
            Diagnostic diagnostic = CreateDiagnostic("message");
            Diagnostic translated = CreateDiagnostic("不同語言的文案");
            DiagnosticReport first = new DiagnosticReport(diagnostic, "execution:1");
            DiagnosticReport duplicate = new DiagnosticReport(translated, "execution:1");
            DiagnosticReport repeatedExecution = new DiagnosticReport(diagnostic, "execution:2");

            Assert.True(sink.TryReport(first));
            Assert.False(sink.TryReport(duplicate));
            Assert.True(sink.TryReport(repeatedExecution));
            Assert.Equal(2, sink.Count);
            Assert.Equal(2L, sink.AcceptedCount);
            Assert.Equal(1L, sink.DuplicateCount);
            Assert.Equal(0L, sink.DroppedCount);
            Assert.Same(first, sink.Reports[0]);
            Assert.Same(repeatedExecution, sink.Reports[1]);
        }

        [Fact]
        public void CapacityEvictsOldestReportsAndBoundsTheIdentityWindow()
        {
            CollectingDiagnosticSink sink = new CollectingDiagnosticSink(2);
            Diagnostic diagnostic = CreateDiagnostic("message");
            DiagnosticReport first = new DiagnosticReport(diagnostic, "1");
            DiagnosticReport second = new DiagnosticReport(diagnostic, "2");
            DiagnosticReport third = new DiagnosticReport(diagnostic, "3");
            sink.Report(first);
            sink.Report(second);
            IReadOnlyList<DiagnosticReport> previousSnapshot = sink.Reports;
            sink.Report(third);

            Assert.Equal(2, sink.Count);
            Assert.Equal(2, sink.Capacity);
            Assert.Same(second, sink.Reports[0]);
            Assert.Same(third, sink.Reports[1]);
            Assert.Equal(1L, sink.DroppedCount);
            Assert.Same(first, previousSnapshot[0]);
            Assert.Same(second, previousSnapshot[1]);
            Assert.True(sink.TryReport(first));
            Assert.Equal(4L, sink.AcceptedCount);
            Assert.Equal(2L, sink.DroppedCount);
            Assert.Equal(0L, sink.DuplicateCount);
            Assert.Same(third, sink.Reports[0]);
            Assert.Same(first, sink.Reports[1]);
        }

        [Fact]
        public void ReportSnapshotCannotBeMutatedThroughCollectionInterfaces()
        {
            CollectingDiagnosticSink sink = new CollectingDiagnosticSink();
            Diagnostic diagnostic = CreateDiagnostic("message");
            DiagnosticReport report = new DiagnosticReport(diagnostic, "1");
            sink.Report(report);
            IList<DiagnosticReport> reports = Assert.IsAssignableFrom<IList<DiagnosticReport>>(sink.Reports);

            Assert.Throws<NotSupportedException>(reports.Clear);
            Assert.Equal(1, sink.Count);
        }

        [Fact]
        public async Task ConcurrentDuplicateDeliveriesPreserveOneOccurrence()
        {
            CollectingDiagnosticSink sink = new CollectingDiagnosticSink(5);
            Diagnostic diagnostic = CreateDiagnostic("message");
            DiagnosticReport report = new DiagnosticReport(diagnostic, "execution:1");
            Task[] deliveries = new Task[20];

            void Deliver()
            {
                sink.Report(report);
            }

            for (int index = 0; index < deliveries.Length; index++)
            {
                deliveries[index] = Task.Run(Deliver);
            }

            await Task.WhenAll(deliveries);

            Assert.Equal(1, sink.Count);
            Assert.Equal(1L, sink.AcceptedCount);
            Assert.Equal(19L, sink.DuplicateCount);
            Assert.Equal(0L, sink.DroppedCount);
        }

        [Fact]
        public async Task ConcurrentDistinctOccurrencesRespectCapacityAndCounts()
        {
            CollectingDiagnosticSink sink = new CollectingDiagnosticSink(5);
            Diagnostic diagnostic = CreateDiagnostic("message");
            Task[] deliveries = new Task[20];
            int occurrence = 0;

            void Deliver()
            {
                int nextOccurrence = Interlocked.Increment(ref occurrence);
                string occurrenceId = nextOccurrence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                DiagnosticReport report = new DiagnosticReport(diagnostic, occurrenceId);
                sink.Report(report);
            }

            for (int index = 0; index < deliveries.Length; index++)
            {
                deliveries[index] = Task.Run(Deliver);
            }

            await Task.WhenAll(deliveries);

            Assert.Equal(5, sink.Count);
            Assert.Equal(20L, sink.AcceptedCount);
            Assert.Equal(0L, sink.DuplicateCount);
            Assert.Equal(15L, sink.DroppedCount);
            HashSet<string> retainedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DiagnosticReport report in sink.Reports)
            {
                Assert.True(retainedIds.Add(report.OccurrenceId));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void CapacityMustBePositive(int capacity)
        {
            void Construct()
            {
                _ = new CollectingDiagnosticSink(capacity);
            }

            Assert.Throws<ArgumentOutOfRangeException>(Construct);
        }

        private static Diagnostic CreateDiagnostic(string message)
        {
            DiagnosticDescriptor descriptor = new DiagnosticDescriptor("module.runtime", "NR1001", DiagnosticSeverity.Error);
            DiagnosticTextSpan span = new DiagnosticTextSpan(4, 2, 1, 5);
            DiagnosticLocation location = new DiagnosticLocation("script:1", textSpan: span);
            return new Diagnostic(descriptor, message, location: location);
        }
    }
}
