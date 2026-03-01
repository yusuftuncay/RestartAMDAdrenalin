namespace RestartAMDAdrenalin.Models;

public sealed class AtomicInt64
{
    // Backing Value
    private long _value;

    // Initialize With the Given Value
    public AtomicInt64(long initialValue)
    {
        _value = initialValue;
    }

    // Thread-Safe Read of the Current Value
    public long Read()
    {
        return Interlocked.Read(ref _value);
    }

    // Atomically Set and Return the Previous Value
    public long Write(long newValue)
    {
        return Interlocked.Exchange(ref _value, newValue);
    }
}
