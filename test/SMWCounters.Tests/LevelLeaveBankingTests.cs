using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// The level-leave verdict wired into the banked counters: a Kept leave banks
// (intro finish, pipe that moved Mario), a NotKept leave discards like a
// death (start+select, side exit). Covers the three code paths: BankedCounter
// via Coins, ExitCounter (its own exit path), and KillCounter (dual tally).
public class LevelLeaveBankingTests
{
    private const int GameMode = 0x0100, Room = 0x010B, OwX = 0x1F17, OwY = 0x1F19,
                      Submap = 0x1F11, OwXh = 0x1F18, OwYh = 0x1F1A,
                      Coins = 0x0DBF, Anim = 0x0071, Midway = 0x13CE, Exits = 0x1F2E,
                      ExitMode = 0x0DD5, Fanfare = 0x0906, Io = 0x1DFB, Boss = 0x13C6,
                      StatusBase = 0x14C8, SpriteBase = 0x009E;
    private const byte Overworld = 0x0E, Level = 0x14, IntroRoom = 0xC5, SomeRoom = 0x1E;

    private static void Map(FakeSnesMemory m, byte x)
    {
        m.SetByte(GameMode, Overworld);
        m.SetByte(OwX, x); m.SetByte(OwY, 20); m.SetByte(Submap, 0); m.SetByte(OwXh, 0); m.SetByte(OwYh, 0);
    }

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
    public void Coins_SilentLeaveBackWhereHeStarted_Reverts()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m, 10); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(Coins, 3); c.Poll(m);
        Assert.Equal(3, c.Value); Assert.True(c.ValueIsAlert);
        Map(m, 10); c.Poll(m);              // start+select / side exit: same tile
        Assert.Equal(0, c.Value); Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Coins_PipeMovedHim_Banks()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m, 10); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(Coins, 3); c.Poll(m);
        Map(m, 40); c.Poll(m);              // pipe-to-map: new tile
        Assert.Equal(3, c.Value); Assert.False(c.ValueIsAlert);
        m.SetByte(Anim, 9); c.Poll(m);      // a later death can't take them
        Assert.Equal(3, c.Value);
    }

    [Fact]
    public void Exits_OrbThenSilentLeave_Reverts_IntroFinish_Banks()
    {
        var c = new ExitCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m, 10); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(Io, 3); c.Poll(m);        // orb: collect, unbanked
        Assert.Equal(1, c.Value); Assert.True(c.ValueIsAlert);
        Map(m, 10); c.Poll(m);              // left without the exit: gone
        Assert.Equal(0, c.Value); Assert.False(c.ValueIsAlert);

        var intro = new ExitCounter(); var m2 = new FakeSnesMemory(); Baseline(m2);
        InLevel(m2, IntroRoom); intro.Poll(m2);   // no map position on record
        m2.SetByte(Io, 3); intro.Poll(m2);
        Assert.True(intro.ValueIsAlert);
        Map(m2, 10); intro.Poll(m2);              // intro finish: kept
        Assert.Equal(1, intro.Value); Assert.False(intro.ValueIsAlert);
    }

    [Fact]
    public void Kills_SilentLeave_RevertsBothTallies_PipeLeave_Banks()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory(); Baseline(m);
        Map(m, 10); c.Poll(m);
        InLevel(m); c.Poll(m);
        m.SetByte(StatusBase, 0x04); c.Poll(m);   // galoomba spinjumped
        c.Mode = KillCountMode.Kills; Assert.Equal(1, c.Value);
        Map(m, 10); c.Poll(m);
        Assert.Equal(0, c.Value);
        c.Mode = KillCountMode.Destruction; Assert.Equal(0, c.Value);

        var k = new KillCounter(); var m2 = new FakeSnesMemory(); Baseline(m2);
        Map(m2, 10); k.Poll(m2);
        InLevel(m2); k.Poll(m2);
        m2.SetByte(StatusBase, 0x04); k.Poll(m2);
        Map(m2, 40); k.Poll(m2);
        k.Mode = KillCountMode.Kills; Assert.Equal(1, k.Value); Assert.False(k.ValueIsAlert);
    }
}
