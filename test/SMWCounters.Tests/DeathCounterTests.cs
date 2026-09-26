using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// Deaths compose the shared DeathEdgeDetector: one count per $0071 rising
// edge to 9, only past the title/file-select screens, and a detach flushes
// the baseline so a reattach can't bridge a stale sample into an edge.
public class DeathCounterTests
{
    private const int Anim = 0x0071, GameMode = 0x0100;
    private const byte Dying = 0x09, LevelMode = 0x14, TitleMode = 0x07;

    private static void Poll(DeathCounter c, FakeSnesMemory m, byte anim, byte mode = LevelMode)
    {
        m.SetByte(Anim, anim);
        m.SetByte(GameMode, mode);
        c.Poll(m);
    }

    [Fact]
    public void CountsEachDeathEdge()
    {
        var c = new DeathCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);
        Poll(c, m, Dying);
        Poll(c, m, Dying);
        Poll(c, m, 0);
        Poll(c, m, Dying);
        Assert.Equal(2, c.Value);
    }

    [Fact]
    public void AttractDemoDeath_DoesNotCount()
    {
        var c = new DeathCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, TitleMode);
        Poll(c, m, Dying, TitleMode);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void Detach_FlushesBaseline_NoBridgedEdge()
    {
        var c = new DeathCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);
        m.Attached = false;
        c.Poll(m);
        m.Attached = true;
        Poll(c, m, Dying);   // first sample after reattach: baseline only
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void Reset_ZeroesAndRebaselines()
    {
        var c = new DeathCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);
        Poll(c, m, Dying);
        c.Reset();
        Assert.Equal(0, c.Value);
        Poll(c, m, Dying);   // still 9 after reset: no edge
        Assert.Equal(0, c.Value);
    }
}
