using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class MoonCounterTests
{
    private const int LevelStart = 0x1935;
    private const int Moon = 0x13C5;

    private static void Poll(MoonCounter c, FakeSnesMemory m, byte levelStart, byte moon)
    {
        m.SetByte(LevelStart, levelStart);
        m.SetByte(Moon, moon);
        c.Poll(m);
    }

    [Fact]
    public void NotInLevel_MoonJump_DoesNotCount()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        // Overworld / load: levelStart != 1, moon byte holds transient data that jumps.
        Poll(c, m, 0, 0);
        Poll(c, m, 0, 5);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void InLevel_MoonCollected_CountsOnce()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        Poll(c, m, 1, 0);  // entered level, 0 moons
        Poll(c, m, 1, 1);  // collected a 3-up moon
        Poll(c, m, 1, 1);  // held
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void EnteringLevel_RebaselinesFromLoadValue()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 3);  // not in level, stale value 3 — ignored
        Poll(c, m, 1, 3);  // enter level; first in-level sample only baselines
        Assert.Equal(0, c.Value);
        Poll(c, m, 1, 4);  // now a real collection
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void LeavingLevel_ClearsBaseline_NoCrossLevelSpuriousCount()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        Poll(c, m, 1, 0);  // in level; baseline
        Poll(c, m, 1, 2);  // real collection
        Assert.Equal(1, c.Value);
        Poll(c, m, 0, 2);  // left level; gate clears the stale baseline
        Poll(c, m, 1, 5);  // first in-level sample after re-entry only re-baselines
        Assert.Equal(1, c.Value);
        Poll(c, m, 1, 6);  // now a real collection in the new level
        Assert.Equal(2, c.Value);
    }
}
