namespace Module.Verification.Invariant
{
    /// <summary>Evaluate a read model without modifying the target. Scheduling and failure policy belong to the caller.</summary>
    public interface IInvariant<in T>
    {
        string Code { get; }
        InvariantResult Evaluate(T context);
    }
}
