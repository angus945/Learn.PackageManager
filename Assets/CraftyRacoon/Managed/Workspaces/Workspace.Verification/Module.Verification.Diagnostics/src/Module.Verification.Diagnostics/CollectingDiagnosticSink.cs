using System;
using System.Collections.Generic;

namespace Module.Verification.Diagnostics
{
    /// <summary>Retains the newest reports and rejects repeated occurrence identifiers within the retained window.</summary>
    public sealed class CollectingDiagnosticSink : IDiagnosticSink
    {
        private readonly object _gate = new object();
        private readonly Queue<DiagnosticReport> _reports = new Queue<DiagnosticReport>();
        private readonly HashSet<string> _occurrenceIds = new HashSet<string>(StringComparer.Ordinal);
        private long _acceptedCount;
        private long _duplicateCount;
        private long _droppedCount;

        public CollectingDiagnosticSink(int capacity = 256)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            Capacity = capacity;
        }

        public int Capacity { get; }
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _reports.Count;
                }
            }
        }

        public IReadOnlyList<DiagnosticReport> Reports
        {
            get
            {
                lock (_gate)
                {
                    DiagnosticReport[] snapshot = _reports.ToArray();
                    return Array.AsReadOnly(snapshot);
                }
            }
        }

        public long AcceptedCount
        {
            get
            {
                lock (_gate)
                {
                    return _acceptedCount;
                }
            }
        }

        public long DuplicateCount
        {
            get
            {
                lock (_gate)
                {
                    return _duplicateCount;
                }
            }
        }

        public long DroppedCount
        {
            get
            {
                lock (_gate)
                {
                    return _droppedCount;
                }
            }
        }

        public void Report(DiagnosticReport report)
        {
            TryReport(report);
        }

        public bool TryReport(DiagnosticReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            lock (_gate)
            {
                if (!_occurrenceIds.Add(report.OccurrenceId))
                {
                    _duplicateCount++;
                    return false;
                }

                if (_reports.Count == Capacity)
                {
                    DiagnosticReport dropped = _reports.Dequeue();
                    _occurrenceIds.Remove(dropped.OccurrenceId);
                    _droppedCount++;
                }

                _reports.Enqueue(report);
                _acceptedCount++;
                return true;
            }
        }
    }
}
