using System;

namespace Module.Verification.RuntimeControl
{
    public sealed class OperationDescriptor
    {
        public OperationDescriptor(string operationType, string idempotencyKey = null, string correlationId = null)
        {
            if (string.IsNullOrWhiteSpace(operationType)) throw new ArgumentException("An operation type is required.", nameof(operationType));
            OperationType = operationType;
            IdempotencyKey = idempotencyKey;
            CorrelationId = correlationId;
        }

        public string OperationType { get; }
        public string IdempotencyKey { get; }
        public string CorrelationId { get; }
    }

    public readonly struct OperationHandle : IEquatable<OperationHandle>
    {
        public OperationHandle(Guid registryId, string scopeId, long epoch, long sequence)
        {
            if (registryId == Guid.Empty) throw new ArgumentException("A registry identity is required.", nameof(registryId));
            if (string.IsNullOrWhiteSpace(scopeId)) throw new ArgumentException("A scope identity is required.", nameof(scopeId));
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            RegistryId = registryId;
            ScopeId = scopeId;
            Epoch = epoch;
            Sequence = sequence;
        }

        public Guid RegistryId { get; }
        public string ScopeId { get; }
        public long Epoch { get; }
        public long Sequence { get; }

        public bool Equals(OperationHandle other)
        {
            return RegistryId == other.RegistryId && Epoch == other.Epoch && Sequence == other.Sequence && string.Equals(ScopeId, other.ScopeId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is OperationHandle && Equals((OperationHandle)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(RegistryId, StringComparer.Ordinal.GetHashCode(ScopeId), Epoch, Sequence);
        }

        public static bool operator ==(OperationHandle left, OperationHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(OperationHandle left, OperationHandle right)
        {
            return !left.Equals(right);
        }
    }

    public enum OperationAdmissionStatus
    {
        Admitted,
        Denied,
        Invalid,
        CapacityExceeded,
        Duplicate
    }

    public sealed class OperationAdmission
    {
        internal OperationAdmission(OperationAdmissionStatus status, string code, OperationHandle handle)
        {
            Status = status;
            Code = code;
            Handle = handle;
        }

        public OperationAdmissionStatus Status { get; }
        public string Code { get; }
        public OperationHandle Handle { get; }
        public bool IsAdmitted
        {
            get { return Status == OperationAdmissionStatus.Admitted || Status == OperationAdmissionStatus.Duplicate; }
        }

        public static OperationAdmission Denied(string code)
        {
            return Rejected(OperationAdmissionStatus.Denied, code);
        }

        public static OperationAdmission Invalid(string code)
        {
            return Rejected(OperationAdmissionStatus.Invalid, code);
        }

        private static OperationAdmission Rejected(OperationAdmissionStatus status, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("An admission code is required.", nameof(code));
            return new OperationAdmission(status, code, default(OperationHandle));
        }
    }

    public enum OperationState
    {
        Pending,
        Running,
        Succeeded,
        Rejected,
        Failed,
        Cancelled
    }

    public sealed class OperationCompletion<TResult>
    {
        public OperationCompletion(OperationState state, string code, TResult result = default(TResult), string observationBarrier = null)
        {
            if (state != OperationState.Succeeded && state != OperationState.Rejected && state != OperationState.Failed && state != OperationState.Cancelled) throw new ArgumentOutOfRangeException(nameof(state));
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A completion code is required.", nameof(code));
            State = state;
            Code = code;
            Result = result;
            ObservationBarrier = observationBarrier;
        }

        public OperationState State { get; }
        public string Code { get; }
        public TResult Result { get; }
        public string ObservationBarrier { get; }
    }

    public enum OperationReadState
    {
        Found,
        Unknown,
        Evicted,
        ForeignHandle
    }

    public sealed class OperationRead<TResult>
    {
        internal OperationRead(OperationReadState readState, OperationHandle handle, OperationDescriptor descriptor, OperationState state, OperationCompletion<TResult> completion)
        {
            ReadState = readState;
            Handle = handle;
            Descriptor = descriptor;
            State = state;
            Completion = completion;
        }

        public OperationReadState ReadState { get; }
        public OperationHandle Handle { get; }
        public OperationDescriptor Descriptor { get; }
        public OperationState State { get; }
        public OperationCompletion<TResult> Completion { get; }
    }

    public sealed class OperationTransition<TResult>
    {
        internal OperationTransition(OperationHandle handle, OperationDescriptor descriptor, OperationState previousState, OperationState state, OperationCompletion<TResult> completion)
        {
            Handle = handle;
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            PreviousState = previousState;
            State = state;
            Completion = completion;
        }

        public OperationHandle Handle { get; }
        public OperationDescriptor Descriptor { get; }
        public OperationState PreviousState { get; }
        public OperationState State { get; }
        public OperationCompletion<TResult> Completion { get; }
    }

    public interface IOperationTransitionObserver<TResult>
    {
        void Observe(OperationTransition<TResult> transition);
    }
}
