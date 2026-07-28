using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class JumpCounterTests
{
    private const int GameMode = 0x0100, Level = 0x1935, Air = 0x0072, Blocked = 0x0077;
    private const byte LevelMainMode = 0x14, OverworldMode = 0x0E;

    private static void Poll(JumpCounter c, FakeSnesMemory m,
        byte gameMode, byte air, byte blocked, byte level = 0)
    {
        m.SetByte(GameMode, gameMode); m.SetByte(Level, level);
        m.SetByte(Air, air); m.SetByte(Blocked, blocked);
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
}
