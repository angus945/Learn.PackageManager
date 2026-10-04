using System;
using System.Collections.Generic;

namespace Module.Verification.Invariant
{
    public sealed class InvariantRegistry<T>
    {
        private readonly SortedDictionary<string, IInvariant<T>> checks = new SortedDictionary<string, IInvariant<T>>(StringComparer.Ordinal);
        public bool IsSealed { get; private set; }
        public int Count => checks.Count;
        public void Register(IInvariant<T> invariant)
        {
            if (IsSealed) throw new InvalidOperationException("Invariant registry is sealed.");
            if (invariant == null || string.IsNullOrWhiteSpace(invariant.Code)) throw new ArgumentException("Invariant requires a code.");
            if (checks.ContainsKey(invariant.Code)) throw new InvalidOperationException("Duplicate invariant code.");
            checks.Add(invariant.Code, invariant);
        }
        public void Seal() => IsSealed = true;
        public IReadOnlyList<InvariantViolation> Evaluate(T context)
        {
            if (!IsSealed) throw new InvalidOperationException("Seal invariants first.");
            List<InvariantViolation> failures = new List<InvariantViolation>();
            foreach (IInvariant<T> invariant in checks.Values)
            {
                InvariantResult result = invariant.Evaluate(context);
                if (result == null) throw new InvalidOperationException("An invariant returned no result.");
                if (!result.IsSatisfied) failures.Add(result.Violation);
            }
            return failures.AsReadOnly();
        }
    }
}
