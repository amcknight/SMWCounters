using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class PowerupCounterTests
{
    private const int GameMode = 0x0100, Anim = 0x0071, Midway = 0x13CE, Exits = 0x1F2E;
    private const byte LevelMainMode = 0x14, OverworldMode = 0x0E;

    private static void Poll(PowerupCounter c, FakeSnesMemory m,
        byte gameMode, byte anim, byte midway, byte exits)
    {
        m.SetByte(GameMode, gameMode); m.SetByte(Anim, anim);
        m.SetByte(Midway, midway); m.SetByte(Exits, exits);
        c.Poll(m);
    }

    [Fact]
    public void CollectInLevel_Bumps_AndAlerts()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 0);   // in level, baseline
        Poll(c, m, LevelMainMode, 2, 0, 0);   // grab mushroom (anim 2)
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
    }

    [Fact]
    public void Collect_CountsWhenGameModeInLevel_EvenIfLevelStartFlagNotSet()
    {
        var c = new PowerupCounter();
        var m = new FakeSnesMemory();
        m.SetByte(0x0100, 0x14);   // in level (game mode = level main), Yoshi-House style
        m.SetByte(0x1935, 0);      // custom level does not set the legacy in-level flag
        m.SetByte(0x0071, 0); c.Poll(m);   // baseline
        m.SetByte(0x0071, 2); c.Poll(m);   // grab mushroom -> should count
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void CollectOffLevel_DoesNotCount()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, OverworldMode, 0, 0, 0);
        Poll(c, m, OverworldMode, 3, 0, 0);   // feather anim while not in level
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void DieBeforeSave_Discards()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 0);
        Poll(c, m, LevelMainMode, 2, 0, 0);   // grab (total 1)
        Poll(c, m, LevelMainMode, 9, 0, 0);   // die
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Midway_Banks()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 0);
        Poll(c, m, LevelMainMode, 4, 0, 0);   // grab flower (total 1)
        Poll(c, m, LevelMainMode, 0, 1, 0);   // hit midway -> banked
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Exit_Banks()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 5);   // baseline exits=5
        Poll(c, m, LevelMainMode, 2, 0, 5);   // grab (total 1)
        Poll(c, m, LevelMainMode, 0, 0, 6);   // $1F2E increments -> banked
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void LowPercentLoop()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 0);
        Poll(c, m, LevelMainMode, 2, 0, 0); Assert.True(c.ValueIsAlert);   // grab
        Poll(c, m, LevelMainMode, 9, 0, 0); Assert.False(c.ValueIsAlert);  // die -> discard
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMainMode, 2, 0, 0); Assert.Equal(1, c.Value);      // grab again
        Poll(c, m, LevelMainMode, 0, 1, 0); Assert.False(c.ValueIsAlert);  // reach midway -> banked
        Assert.Equal(1, c.Value);
    }
}
