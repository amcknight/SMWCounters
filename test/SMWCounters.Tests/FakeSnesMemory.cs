using System.Collections.Generic;
using LiveSplit.SmwCounters.Snes;

namespace SMWCounters.Tests;

// Test ISnesMemory: attach state + a mutable WRAM byte map the test drives.
internal sealed class FakeSnesMemory : ISnesMemory
{
    private readonly Dictionary<int, byte> bytes = new();

    public bool Attached { get; set; } = true;
    public bool IsAttached => Attached;

    public void SetByte(int snesOffset, byte value) => bytes[snesOffset] = value;

    public bool ReadWramByte(int snesOffset, out byte value)
        => bytes.TryGetValue(snesOffset, out value);
}
