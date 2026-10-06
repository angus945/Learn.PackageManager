namespace Module.Verification.TraceBuffer
{
    public interface ITraceWriter<in T>
    {
        void Append(T item);
    }

    public interface ITraceReader<T>
    {
        TraceRead<T> Read(TraceCursor cursor, int maxItems);
    }

    public interface ITraceBuffer<T> : ITraceWriter<T>, ITraceReader<T>
    {
    }
}
