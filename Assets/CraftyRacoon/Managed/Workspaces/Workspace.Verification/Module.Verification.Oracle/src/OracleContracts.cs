using System;
using System.Collections.Generic;

namespace Module.Verification.Oracle
{
    public enum TestVerdict
    {
        Passed,
        Failed,
        Inconclusive,
        InfrastructureError,
        Skipped
    }

    public sealed class OracleResult
    {
        public OracleResult(TestVerdict verdict, string code, string detail = "")
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("An oracle result code is required.", nameof(code));
            Verdict = verdict;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public TestVerdict Verdict { get; }
        public string Code { get; }
        public string Detail { get; }
    }

    public sealed class EvaluationError
    {
        public EvaluationError(string oracleId, string exceptionType, string message)
        {
            OracleId = oracleId ?? throw new ArgumentNullException(nameof(oracleId));
            ExceptionType = exceptionType ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string OracleId { get; }
        public string ExceptionType { get; }
        public string Message { get; }
    }

    public interface ITestOracle<TContext>
    {
        string Id { get; }
        OracleResult Evaluate(TContext context);
    }

    public sealed class EvaluationReport
    {
        public EvaluationReport(string oracleSetId, string contextId, TestVerdict verdict, IEnumerable<OracleResult> results, IEnumerable<EvaluationError> errors)
        {
            if (string.IsNullOrWhiteSpace(oracleSetId)) throw new ArgumentException("An oracle set identity is required.", nameof(oracleSetId));
            OracleSetId = oracleSetId;
            ContextId = contextId ?? string.Empty;
            Verdict = verdict;
            Results = new List<OracleResult>(results ?? throw new ArgumentNullException(nameof(results))).AsReadOnly();
            Errors = new List<EvaluationError>(errors ?? throw new ArgumentNullException(nameof(errors))).AsReadOnly();
        }

        public string OracleSetId { get; }
        public string ContextId { get; }
        public TestVerdict Verdict { get; }
        public IReadOnlyList<OracleResult> Results { get; }
        public IReadOnlyList<EvaluationError> Errors { get; }
    }
}
