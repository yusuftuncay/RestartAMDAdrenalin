namespace RestartAMDAdrenalin.Models;

public sealed class AtomicInt32
{
    private int _value;

    public AtomicInt32(int initialValue)
    {
        _value = initialValue;
    }

    public int Exchange(int newValue)
    {
        return Interlocked.Exchange(ref _value, newValue);
    }

    public int Value
    {
        get { return Interlocked.CompareExchange(ref _value, 0, 0); }
    }
}
