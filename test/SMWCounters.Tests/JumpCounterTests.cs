using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class JumpCounterTests
{
    private const int GameMode = 0x0100, Level = 0x1935, Air = 0x0072, Blocked = 0x0077,
                      Anim = 0x0071, Midway = 0x13CE, Exits = 0x1F2E;
    private const byte LevelMainMode = 0x14, OverworldMode = 0x0E;

    private static void Poll(JumpCounter c, FakeSnesMemory m,
        byte gameMode, byte air, byte blocked, byte level = 0,
        byte anim = 0, byte midway = 0, byte exits = 0)
    {
        m.SetByte(GameMode, gameMode); m.SetByte(Level, level);
        m.SetByte(Air, air); m.SetByte(Blocked, blocked);
        m.SetByte(Anim, anim); m.SetByte(Midway, midway); m.SetByte(Exits, exits);
        c.Poll(m);
    }

    [Fact]
    public void JumpInLevel_Counts()
    {
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0x00, 0x04, level: 1);   // on ground, blocked-below
        Poll(c, m, LevelMainMode, 0x0B, 0x00, level: 1);   // rising
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void JumpInCustomYoshiHouse_Counts_WithoutLegacyLevelFlag()
    {
        // Regression (2026-07-27): $1935 stays 0 in custom Yoshi Houses, but
        // game mode $0100 == 0x14 — jumps there must count.
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0x00, 0x04);   // level flag defaults to 0
        Poll(c, m, LevelMainMode, 0x0B, 0x00);
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void JumpOffLevel_DoesNotCount()
    {
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, OverworldMode, 0x00, 0x04);   // overworld / title demo
        Poll(c, m, OverworldMode, 0x0B, 0x00);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void DieBeforeBank_RevertsJumps()
    {
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0x00, 0x04);
        Poll(c, m, LevelMainMode, 0x0B, 0x00);              // jump: total 1
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);                        // unbanked -> gold
        Poll(c, m, LevelMainMode, 0x24, 0x00, anim: 9);     // die -> revert
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Midway_BanksJumps_SurvivingDeath()
    {
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0x00, 0x04);
        Poll(c, m, LevelMainMode, 0x0B, 0x00);                     // jump: total 1
        Poll(c, m, LevelMainMode, 0x24, 0x00, midway: 1);          // midway -> banked
        Assert.False(c.ValueIsAlert);
        Poll(c, m, LevelMainMode, 0x24, 0x00, midway: 1, anim: 9); // die after bank
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void Exit_BanksJumps()
    {
        var c = new JumpCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0x00, 0x04, exits: 5);
        Poll(c, m, LevelMainMode, 0x0B, 0x00, exits: 5);   // jump: total 1
        Poll(c, m, LevelMainMode, 0x24, 0x00, exits: 6);   // $1F2E increments -> banked
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void BankedOff_JumpsStickThroughDeath()
    {
        var c = new JumpCounter { Banked = false };
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0x00, 0x04);
        Poll(c, m, LevelMainMode, 0x0B, 0x00);              // jump: total 1
        Assert.False(c.ValueIsAlert);                       // plain tally, no gold
        Poll(c, m, LevelMainMode, 0x24, 0x00, anim: 9);     // die: no revert
        Assert.Equal(1, c.Value);
    }
}
