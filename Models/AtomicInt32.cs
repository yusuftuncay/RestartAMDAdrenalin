namespace RestartAMDAdrenalin.Models;

public sealed class AtomicInt32
{
    // Backing Value
    private int _value;

    // Initialize With the Given Value
    public AtomicInt32(int initialValue)
    {
        _value = initialValue;
    }

    // Atomically Set and Return the Previous Value
    public int Exchange(int newValue)
    {
        return Interlocked.Exchange(ref _value, newValue);
    }

    // Thread-Safe Read of the Current Value
    public int Value
    {
        get { return Interlocked.CompareExchange(ref _value, 0, 0); }
    }
}
