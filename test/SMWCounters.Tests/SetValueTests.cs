using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class SetValueTests
{
    [Fact]
    public void SimpleCounter_SetValue_SetsValue()
    {
        var c = new DeathCounter();
        c.SetValue(37);
        Assert.Equal(37, c.Value);
    }

    [Fact]
    public void BankedCounter_SetValue_SetsValueAndClearsAlert()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 10, 0);   // baseline (exits already 10 from save)
        Poll(c, m, 1, 4, 0, 10, 0);   // fanfare 0->1, io=4 goal, boss alive: total=1
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);  // real alert: collected but not yet banked

        c.SetValue(12);
        Assert.Equal(12, c.Value);
        Assert.False(c.ValueIsAlert);   // total == saved
    }

    // Full poll: fanfare ($0906), io ($1DFB), bossDefeat ($13C6),
    // exitsCompleted ($1F2E), death anim ($0071). Mirrors ExitCounterTests.Poll.
    private static void Poll(ExitCounter c, FakeSnesMemory m,
        byte fanfare, byte io, byte bossDefeat, byte exits, byte anim)
    {
        m.SetByte(0x0906, fanfare);
        m.SetByte(0x1DFB, io);
        m.SetByte(0x13C6, bossDefeat);
        m.SetByte(0x1F2E, exits);
        m.SetByte(0x0071, anim);
        c.Poll(m);
    }
}
