namespace LiveSplit.SmwCounters.Counters;

// Tracks the previous reading of a byte across poll ticks so a counter can
// detect edges (rising, transition-to-value, value-increase) against the
// last seen sample. Cleared whenever the memory source goes away so a
// reattach doesn't fabricate a transition from stale state.
internal sealed class PreviousByte
{
    public bool HasPrevious { get; private set; }
    public byte Value { get; private set; }

    public void Set(byte value)
    {
        Value = value;
        HasPrevious = true;
    }

    public void Clear() => HasPrevious = false;
}
