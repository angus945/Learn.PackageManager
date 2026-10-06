using System;
using System.Collections.Generic;

namespace Module.Verification.RuntimeControl
{
    public sealed class OperationRegistry<TResult>
    {
        private sealed class Entry
        {
            public Entry(OperationHandle handle, OperationDescriptor descriptor)
            {
                Handle = handle;
                Descriptor = descriptor;
                State = OperationState.Pending;
            }

            public OperationHandle Handle { get; }
            public OperationDescriptor Descriptor { get; }
            public OperationState State { get; set; }
            public OperationCompletion<TResult> Completion { get; set; }
        }

        private readonly object gate = new object();
        private readonly Guid registryId = Guid.NewGuid();
        private readonly string scopeId;
        private readonly long epoch;
        private readonly int maxPending;
        private readonly int maxResults;
        private readonly IOperationTransitionObserver<TResult> transitionObserver;
        private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        private readonly Dictionary<string, long> idempotency = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Queue<long> completed = new Queue<long>();
        private long nextSequence;
        private long highestEvictedSequence;
        private int pendingCount;

        public OperationRegistry(string scopeId, long epoch, int maxPending = 1024, int maxResults = 4096, IOperationTransitionObserver<TResult> transitionObserver = null)
        {
            if (string.IsNullOrWhiteSpace(scopeId)) throw new ArgumentException("A scope identity is required.", nameof(scopeId));
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch));
            if (maxPending < 1) throw new ArgumentOutOfRangeException(nameof(maxPending));
            if (maxResults < 1) throw new ArgumentOutOfRangeException(nameof(maxResults));
            this.scopeId = scopeId;
            this.epoch = epoch;
            this.maxPending = maxPending;
            this.maxResults = maxResults;
            this.transitionObserver = transitionObserver;
        }

        public OperationAdmission Admit(OperationDescriptor descriptor)
        {
            if (descriptor == null) return OperationAdmission.Invalid("operation.invalid");
            lock (gate)
            {
                long existingSequence;
                if (!string.IsNullOrEmpty(descriptor.IdempotencyKey) && idempotency.TryGetValue(descriptor.IdempotencyKey, out existingSequence))
                {
                    Entry existing = entries[existingSequence];
                    bool sameOperationType = string.Equals(existing.Descriptor.OperationType, descriptor.OperationType, StringComparison.Ordinal);
                    bool sameCorrelation = string.Equals(existing.Descriptor.CorrelationId, descriptor.CorrelationId, StringComparison.Ordinal);
                    if (!sameOperationType || !sameCorrelation) return OperationAdmission.Invalid("operation.idempotency_conflict");
                    return new OperationAdmission(OperationAdmissionStatus.Duplicate, "operation.duplicate", existing.Handle);
                }
                if (pendingCount >= maxPending) return new OperationAdmission(OperationAdmissionStatus.CapacityExceeded, "operation.pending_capacity", default(OperationHandle));
                nextSequence = checked(nextSequence + 1);
                OperationHandle handle = new OperationHandle(registryId, scopeId, epoch, nextSequence);
                Entry entry = new Entry(handle, descriptor);
                entries.Add(handle.Sequence, entry);
                if (!string.IsNullOrEmpty(descriptor.IdempotencyKey)) idempotency.Add(descriptor.IdempotencyKey, handle.Sequence);
                pendingCount++;
                Notify(new OperationTransition<TResult>(handle, descriptor, default(OperationState), OperationState.Pending, null));
                return new OperationAdmission(OperationAdmissionStatus.Admitted, "operation.admitted", handle);
            }
        }

        public bool TryMarkRunning(OperationHandle handle)
        {
            lock (gate)
            {
                Entry entry;
                if (!TryGetOwned(handle, out entry) || entry.State != OperationState.Pending) return false;
                OperationState previousState = entry.State;
                entry.State = OperationState.Running;
                Notify(new OperationTransition<TResult>(entry.Handle, entry.Descriptor, previousState, entry.State, null));
                return true;
            }
        }

        public bool TryComplete(OperationHandle handle, OperationCompletion<TResult> completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            lock (gate)
            {
                Entry entry;
                if (!TryGetOwned(handle, out entry)) return false;
                if (entry.State != OperationState.Pending && entry.State != OperationState.Running) return false;
                OperationState previousState = entry.State;
                entry.State = completion.State;
                entry.Completion = completion;
                pendingCount--;
                completed.Enqueue(handle.Sequence);
                Notify(new OperationTransition<TResult>(entry.Handle, entry.Descriptor, previousState, entry.State, completion));
                TrimResults();
                return true;
            }
        }

        public OperationRead<TResult> Read(OperationHandle handle)
        {
            lock (gate)
            {
                if (handle.RegistryId != registryId || handle.Epoch != epoch || !string.Equals(handle.ScopeId, scopeId, StringComparison.Ordinal)) return Empty(OperationReadState.ForeignHandle, handle);
                Entry entry;
                if (entries.TryGetValue(handle.Sequence, out entry)) return new OperationRead<TResult>(OperationReadState.Found, entry.Handle, entry.Descriptor, entry.State, entry.Completion);
                if (handle.Sequence <= highestEvictedSequence) return Empty(OperationReadState.Evicted, handle);
                return Empty(OperationReadState.Unknown, handle);
            }
        }

        private bool TryGetOwned(OperationHandle handle, out Entry entry)
        {
            if (handle.RegistryId != registryId || handle.Epoch != epoch || !string.Equals(handle.ScopeId, scopeId, StringComparison.Ordinal))
            {
                entry = null;
                return false;
            }
            return entries.TryGetValue(handle.Sequence, out entry);
        }

        private void TrimResults()
        {
            while (completed.Count > maxResults)
            {
                long sequence = completed.Dequeue();
                Entry removed = entries[sequence];
                entries.Remove(sequence);
                if (!string.IsNullOrEmpty(removed.Descriptor.IdempotencyKey)) idempotency.Remove(removed.Descriptor.IdempotencyKey);
                if (sequence > highestEvictedSequence) highestEvictedSequence = sequence;
            }
        }

        private void Notify(OperationTransition<TResult> transition)
        {
            if (transitionObserver == null) return;
            try
            {
                transitionObserver.Observe(transition);
            }
            catch (Exception)
            {
            }
        }

        private static OperationRead<TResult> Empty(OperationReadState state, OperationHandle handle)
        {
            return new OperationRead<TResult>(state, handle, null, default(OperationState), null);
        }
    }
}
