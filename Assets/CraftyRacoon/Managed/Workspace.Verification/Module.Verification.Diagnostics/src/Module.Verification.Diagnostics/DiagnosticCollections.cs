#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Module.Verification.Diagnostics
{
    internal static class DiagnosticCollections
    {
        internal static IReadOnlyDictionary<string, string> CopyDictionary(IReadOnlyDictionary<string, string>? source, string parameterName)
        {
            Dictionary<string, string> copy = new Dictionary<string, string>(StringComparer.Ordinal);
            if (source != null)
            {
                foreach (KeyValuePair<string, string> entry in source)
                {
                    if (entry.Key == null || entry.Value == null)
                    {
                        throw new ArgumentException("Diagnostic dictionaries cannot contain null keys or values.", parameterName);
                    }

                    copy.Add(entry.Key, entry.Value);
                }
            }

            return new ReadOnlyDictionary<string, string>(copy);
        }

        internal static IReadOnlyList<T> CopyList<T>(IReadOnlyList<T>? source, string parameterName) where T : class
        {
            int count = source?.Count ?? 0;
            T[] copy = new T[count];
            for (int index = 0; index < count; index++)
            {
                T item = source![index];
                if (item == null)
                {
                    throw new ArgumentException("Diagnostic collections cannot contain null entries.", parameterName);
                }

                copy[index] = item;
            }

            return Array.AsReadOnly(copy);
        }

        internal static void ValidateSeverity(DiagnosticSeverity severity, string parameterName)
        {
            if (severity != DiagnosticSeverity.Info && severity != DiagnosticSeverity.Warning && severity != DiagnosticSeverity.Error)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
