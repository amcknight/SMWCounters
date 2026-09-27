using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// Death is the only discard. Any other return to the map banks the collect
// counters (start+select keeps the mushroom, the coins, the moons); Exits
// bank on the intro finish and revert an abandoned finish. Covers the three
// code paths: BankedCounter via Coins, ExitCounter, and KillCounter.
public class LevelLeaveBankingTests
{
    private const int GameMode = 0x0100, Room = 0x010B,
                      Coins = 0x0DBF, Anim = 0x0071, Midway = 0x13CE, Exits = 0x1F2E,
                      ExitMode = 0x0DD5, Fanfare = 0x0906, Io = 0x1DFB, Boss = 0x13C6,
                      StatusBase = 0x14C8, SpriteBase = 0x009E;
    private const byte Overworld = 0x0E, Level = 0x14, IntroRoom = 0xC5, SomeRoom = 0x1E;

    private static void Map(FakeSnesMemory m) => m.SetByte(GameMode, Overworld);

    private static void InLevel(FakeSnesMemory m, byte room = SomeRoom)
    {
        m.SetByte(GameMode, Level);
        m.SetByte(Room, room);
    }

    private static void Baseline(FakeSnesMemory m)
    {
        m.SetByte(Coins, 0); m.SetByte(Anim, 0); m.SetByte(Midway, 0); m.SetByte(Exits, 5);
        m.SetByte(ExitMode, 0); m.SetByte(Fanfare, 0); m.SetByte(Io, 0); m.SetByte(Boss, 0);
        m.SetByte(StatusBase, 0x08); m.SetByte(SpriteBase, 0x0F);
        m.SetByte(Room, SomeRoom);
    }

    [Fact]
    public void Coins_StartSelect_Banks_ThenALaterDeathCannotTakeThem()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(Coins, 3); c.Poll(m);
        Assert.Equal(3, c.Value); Assert.True(c.ValueIsAlert);
        Map(m); c.Poll(m);                  // start+select / side exit / pipe: kept
        Assert.Equal(3, c.Value); Assert.False(c.ValueIsAlert);
        InLevel(m); c.Poll(m);
        m.SetByte(Anim, 9); c.Poll(m);      // die in the next level
        Assert.Equal(3, c.Value);
    }

    [Fact]
    public void Coins_DeathBeforeLeaving_StillReverts()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(Coins, 3); c.Poll(m);
        m.SetByte(Anim, 9); c.Poll(m);      // die
        Assert.Equal(0, c.Value);
        Map(m); c.Poll(m);                  // the arrival banks nothing new
        Assert.Equal(0, c.Value); Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Exits_AbandonedOrb_Reverts_IntroFinish_Banks()
    {
        var c = new ExitCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(Io, 3); c.Poll(m);        // orb: collect, unbanked
        Assert.Equal(1, c.Value); Assert.True(c.ValueIsAlert);
        Map(m); c.Poll(m);                  // back on the map with no exit event
        Assert.Equal(0, c.Value); Assert.False(c.ValueIsAlert);

        var intro = new ExitCounter(); var m2 = new FakeSnesMemory(); Baseline(m2);
        InLevel(m2, IntroRoom); intro.Poll(m2);
        m2.SetByte(Io, 3); intro.Poll(m2);
        Assert.True(intro.ValueIsAlert);
        Map(m2); intro.Poll(m2);            // intro finish: kept
        Assert.Equal(1, intro.Value); Assert.False(intro.ValueIsAlert);
    }

    [Fact]
    public void Exits_ExitEventThenArrival_StaysBanked()
    {
        var c = new ExitCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(Fanfare, 1); m.SetByte(Io, 4); c.Poll(m);   // goal
        Assert.True(c.ValueIsAlert);
        m.SetByte(GameMode, 0x0C); m.SetByte(ExitMode, 1); c.Poll(m);   // exit event at 0C
        Assert.False(c.ValueIsAlert);
        Map(m); c.Poll(m);
        Assert.Equal(1, c.Value); Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Kills_StartSelect_BanksBothTallies()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(StatusBase, 0x04); c.Poll(m);   // galoomba spinjumped
        c.Mode = KillCountMode.Kills; Assert.Equal(1, c.Value); Assert.True(c.ValueIsAlert);
        Map(m); c.Poll(m);
        Assert.Equal(1, c.Value); Assert.False(c.ValueIsAlert);
        c.Mode = KillCountMode.Destruction; Assert.Equal(1, c.Value); Assert.False(c.ValueIsAlert);
    }
}
