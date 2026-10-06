using System;
using System.Collections.Generic;

namespace Module.Verification.Oracle
{
    public sealed class OracleSet<TContext>
    {
        private readonly IReadOnlyList<ITestOracle<TContext>> oracles;

        public OracleSet(string id, IEnumerable<ITestOracle<TContext>> oracles)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An oracle set identity is required.", nameof(id));
            if (oracles == null) throw new ArgumentNullException(nameof(oracles));
            List<ITestOracle<TContext>> ordered = new List<ITestOracle<TContext>>();
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (ITestOracle<TContext> oracle in oracles)
            {
                if (oracle == null) throw new ArgumentException("Oracle sets cannot contain null entries.", nameof(oracles));
                if (string.IsNullOrWhiteSpace(oracle.Id)) throw new ArgumentException("Every oracle requires an identity.", nameof(oracles));
                if (!identities.Add(oracle.Id)) throw new ArgumentException("Oracle identities must be unique.", nameof(oracles));
                ordered.Add(oracle);
            }
            Id = id;
            this.oracles = ordered.AsReadOnly();
        }

        public string Id { get; }
        public IReadOnlyList<ITestOracle<TContext>> Oracles
        {
            get { return oracles; }
        }

        public EvaluationReport Evaluate(string contextId, TContext context)
        {
            List<OracleResult> results = new List<OracleResult>();
            List<EvaluationError> errors = new List<EvaluationError>();
            TestVerdict aggregate = oracles.Count == 0 ? TestVerdict.Skipped : TestVerdict.Passed;
            foreach (ITestOracle<TContext> oracle in oracles)
            {
                try
                {
                    OracleResult result = oracle.Evaluate(context);
                    if (result == null) throw new InvalidOperationException("An oracle returned no result.");
                    results.Add(result);
                    aggregate = Combine(aggregate, result.Verdict);
                }
                catch (Exception exception)
                {
                    errors.Add(new EvaluationError(oracle.Id, exception.GetType().FullName, exception.Message));
                    aggregate = TestVerdict.InfrastructureError;
                }
            }
            return new EvaluationReport(Id, contextId, aggregate, results, errors);
        }

        private static TestVerdict Combine(TestVerdict left, TestVerdict right)
        {
            return Rank(right) > Rank(left) ? right : left;
        }

        private static int Rank(TestVerdict verdict)
        {
            if (verdict == TestVerdict.InfrastructureError) return 5;
            if (verdict == TestVerdict.Failed) return 4;
            if (verdict == TestVerdict.Inconclusive) return 3;
            if (verdict == TestVerdict.Passed) return 2;
            return 1;
        }
    }
}
