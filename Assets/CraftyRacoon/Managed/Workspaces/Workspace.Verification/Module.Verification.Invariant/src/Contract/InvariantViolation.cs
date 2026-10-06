using System.Runtime.Serialization;

namespace Module.Verification.Invariant
{
    [DataContract(Name = "InvariantViolation", Namespace = "http://schemas.datacontract.org/2004/07/Testability")]
    public sealed class InvariantViolation
    {
        public InvariantViolation(string code, string detail) { Code = code; Detail = detail; }
        [DataMember(Order = 1)] public string Code { get; private set; }
        [DataMember(Order = 2)] public string Detail { get; private set; }
    }

    public sealed class InvariantResult
    {
        private InvariantResult(bool isSatisfied, InvariantViolation violation)
        {
            IsSatisfied = isSatisfied;
            Violation = violation;
        }

        public bool IsSatisfied { get; }
        public InvariantViolation Violation { get; }

        public static InvariantResult Satisfied()
        {
            return new InvariantResult(true, null);
        }

        public static InvariantResult Violated(InvariantViolation violation)
        {
            if (violation == null) throw new System.ArgumentNullException(nameof(violation));
            return new InvariantResult(false, violation);
        }
    }
}
