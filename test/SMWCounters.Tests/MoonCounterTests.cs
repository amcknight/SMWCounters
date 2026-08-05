using System.Xml;

using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class MoonCounterTests
{
    private const int GameMode = 0x0100;
    private const int Moon = 0x13C5;
    private const int Anim = 0x0071;
    private const int Midway = 0x13CE;

    private const byte LevelMain = 0x14;

    private static void Poll(MoonCounter c, FakeSnesMemory m, byte gameMode, byte moon,
                             byte anim = 0, byte midway = 0)
    {
        m.SetByte(GameMode, gameMode);
        m.SetByte(Moon, moon);
        m.SetByte(Anim, anim);
        m.SetByte(Midway, midway);
        c.Poll(m);
    }

    [Fact]
    public void NotInLevel_MoonJump_DoesNotCount()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        // Overworld / load: game mode != level-main, moon byte holds transient data that jumps.
        Poll(c, m, 0, 0);
        Poll(c, m, 0, 5);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void InLevel_MoonCollected_CountsOnce()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);  // entered level, 0 moons
        Poll(c, m, LevelMain, 1);  // collected a 3-up moon
        Poll(c, m, LevelMain, 1);  // held
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void CustomYoshiHouse_LegacyInLevelFlagUnset_StillCounts()
    {
        var c = new MoonCounter();
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
        var c = new MoonCounter();
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
        var c = new MoonCounter();
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
    public void Banked_MoonIsGoldUntilMidway_ThenLocked()
    {
        var c = new MoonCounter();              // Banked defaults true at counter level
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);               // collect: unbanked
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
        Poll(c, m, LevelMain, 1, midway: 1);    // midway 0->1 banks
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
        Poll(c, m, LevelMain, 1, anim: 9);      // die after the bank: kept
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void Banked_DeathBeforeBank_DiscardsTheMoon()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);               // collect: unbanked
        Poll(c, m, LevelMain, 1, anim: 9);      // die before banking
        Assert.Equal(0, c.Value);
        c.Banked = false;                        // plain history still has it
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void ToggleOff_MoonsBehaveLikeTheOldPlainTally()
    {
        var c = new MoonCounter { Banked = false };  // the settings default for moons
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);            // plain view never alerts
        Poll(c, m, LevelMain, 1, anim: 9);       // die: plain view unaffected
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void LegacyLayoutWithDedupeElements_LoadsValueAndIgnoresThem()
    {
        var doc = new XmlDocument();
        XmlElement parent = doc.CreateElement("state");
        XmlElement moons = doc.CreateElement("Moons");
        moons.InnerText = "4";
        parent.AppendChild(moons);
        XmlElement mode = doc.CreateElement("DedupeMode");  // pre-v0.6 element
        mode.InnerText = "PerLevel";
        parent.AppendChild(mode);

        var c = new MoonCounter();
        c.LoadState(parent);
        Assert.Equal(4, c.Value);                // banked view (saved defaulted to 4)
        Assert.False(c.ValueIsAlert);
        c.Banked = false;
        Assert.Equal(4, c.Value);                // plain defaulted to total
    }
}
