namespace RestartAMDAdrenalin.Models;

public sealed class AtomicInt64
{
    private long _value;

    public AtomicInt64(long initialValue)
    {
        _value = initialValue;
    }

    public long Read()
    {
        return Interlocked.Read(ref _value);
    }

    public long Write(long newValue)
    {
        return Interlocked.Exchange(ref _value, newValue);
    }
}
