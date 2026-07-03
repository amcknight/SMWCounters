namespace LiveSplit.SmwCounters.Snes;

internal interface ISnesMemory
{
    bool IsAttached { get; }
    bool ReadWramByte(int snesOffset, out byte value);
}
