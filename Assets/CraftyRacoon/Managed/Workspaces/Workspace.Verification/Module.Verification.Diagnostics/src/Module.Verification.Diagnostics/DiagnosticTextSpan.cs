#nullable enable

using System;

namespace Module.Verification.Diagnostics
{
    /// <summary>A zero-based UTF-16 range with an exclusive end and optional one-based display coordinates.</summary>
    public sealed class DiagnosticTextSpan
    {
        public DiagnosticTextSpan(int start, int length, int? line = null, int? column = null)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }

            if (length < 0 || length > int.MaxValue - start)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if (line.HasValue && line.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(line));
            }

            if (column.HasValue && column.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            Start = start;
            Length = length;
            End = start + length;
            Line = line;
            Column = column;
        }

        public int Start { get; }
        public int Length { get; }
        public int End { get; }
        public int? Line { get; }
        public int? Column { get; }
    }
}
