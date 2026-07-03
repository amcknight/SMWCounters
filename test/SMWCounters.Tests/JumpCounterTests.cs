using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class JumpCounterTests
{
    private const int Level = 0x1935, Air = 0x0072, Blocked = 0x0077;

    private static void Poll(JumpCounter c, FakeSnesMemory m, byte level, byte air, byte blocked)
    {
        m.SetByte(Level, level); m.SetByte(Air, air); m.SetByte(Blocked, blocked);
        c.Poll(m);
    }

    [Fact]
    public void JumpInLevel_Counts()
    {
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 1, 0x00, 0x04);   // on ground, blocked-below
        Poll(c, m, 1, 0x0B, 0x00);   // rising
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void JumpOffLevel_DoesNotCount()
    {
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 0, 0x00, 0x04);   // not in level (e.g. title demo / overworld)
        Poll(c, m, 0, 0x0B, 0x00);
        Assert.Equal(0, c.Value);
    }
}
