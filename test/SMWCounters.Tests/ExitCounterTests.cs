using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class ExitCounterTests
{
    private static void Poll(ExitCounter c, FakeSnesMemory m, byte exitMode)
    {
        m.SetByte(0x0DD5, exitMode);
        c.Poll(m);
    }

    [Fact]
    public void SaveLoad_DoesNotCount()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        // A save load leaves exitMode at 0 (it is an in-level end-of-level byte).
        Poll(c, m, 0);
        Poll(c, m, 0);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void ExitEvent_CountsOnce()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);   // in level, no exit yet
        Poll(c, m, 4);   // goal reached: exitMode shifts to a valid value
        Poll(c, m, 4);   // held: no double count
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void ExitMode128_IsIgnored()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);
        Poll(c, m, 128); // 128 is not a countable exit
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void TwoExits_CountTwice()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);
        Poll(c, m, 4);   // exit 1
        Poll(c, m, 0);   // back to play
        Poll(c, m, 7);   // exit 2 (e.g. key)
        Assert.Equal(2, c.Value);
    }

    [Fact]
    public void Detach_ClearsBaseline_NoPhantomOnReattach()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 4);           // baseline established at 4
        m.Attached = false; c.Poll(m);
        m.Attached = true;
        Poll(c, m, 7);           // first post-reattach sample only re-baselines
        Assert.Equal(0, c.Value);
    }
}
