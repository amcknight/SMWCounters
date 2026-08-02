using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class MoonCounterTests
{
    private const int GameMode = 0x0100;
    private const int Moon = 0x13C5;
    private const int LevelNum = 0x13BF;

    private const byte LevelMain = 0x14;

    private static void Poll(MoonCounter c, FakeSnesMemory m, byte gameMode, byte moon)
    {
        m.SetByte(GameMode, gameMode);
        m.SetByte(Moon, moon);
        c.Poll(m);
    }

    private static void Poll(MoonCounter c, FakeSnesMemory m, byte gameMode, byte moon, byte level)
    {
        m.SetByte(GameMode, gameMode);
        m.SetByte(Moon, moon);
        m.SetByte(LevelNum, level);
        c.Poll(m);
    }

    [Fact]
    public void NotInLevel_MoonJump_DoesNotCount()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        // Overworld / load: game mode != level-main, moon byte holds transient data that jumps.
        Poll(c, m, 0, 0);
        Poll(c, m, 0, 5);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void InLevel_MoonCollected_CountsOnce()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);  // entered level, 0 moons
        Poll(c, m, LevelMain, 1);  // collected a 3-up moon
        Poll(c, m, LevelMain, 1);  // held
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void CustomYoshiHouse_LegacyInLevelFlagUnset_StillCounts()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        // Regression (2026-07-27): $1935 stays 0 in custom Yoshi Houses, but
        // game mode is level-main — moons there must still count.
        m.SetByte(0x1935, 0);
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void EnteringLevel_RebaselinesFromLoadValue()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 3);          // not in level, stale value 3 — ignored
        Poll(c, m, LevelMain, 3);  // enter level; first in-level sample only baselines
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMain, 4);  // now a real collection
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void LeavingLevel_ClearsBaseline_NoCrossLevelSpuriousCount()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.All };
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);  // in level; baseline
        Poll(c, m, LevelMain, 2);  // real collection
        Assert.Equal(1, c.Value);
        Poll(c, m, 0, 2);          // left level; gate clears the stale baseline
        Poll(c, m, LevelMain, 5);  // first in-level sample after re-entry only re-baselines
        Assert.Equal(1, c.Value);
        Poll(c, m, LevelMain, 6);  // now a real collection in the new level
        Assert.Equal(2, c.Value);
    }

    [Fact]
    public void PerLevel_SameLevel_MoonCountedOnce()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.PerLevel };
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0, 5);  // enter level 5; baseline
        Poll(c, m, LevelMain, 1, 5);  // first moon-count increase in level 5 -> counts
        Assert.Equal(1, c.Value);
        Poll(c, m, LevelMain, 2, 5);  // another increase in the same level -> already counted, no change
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void PerLevel_DifferentLevel_CountsSeparately()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.PerLevel };
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0, 5);  // enter level 5; baseline
        Poll(c, m, LevelMain, 1, 5);  // moon collected in level 5
        Assert.Equal(1, c.Value);
        Poll(c, m, 0, 1, 5);          // leave level; baseline cleared
        Poll(c, m, LevelMain, 1, 9);  // enter level 9; first in-level sample only re-baselines
        Assert.Equal(1, c.Value);
        Poll(c, m, LevelMain, 2, 9);  // moon collected in level 9 -> counts separately
        Assert.Equal(2, c.Value);
    }
}
