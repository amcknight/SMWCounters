using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class CoinCounterTests
{
    private const int GameMode = 0x0100, Coins = 0x0DBF, Anim = 0x0071,
                      Midway = 0x13CE, Exits = 0x1F2E;
    private const byte LevelMainMode = 0x14, OverworldMode = 0x0E;

    private static void Poll(CoinCounter c, FakeSnesMemory m,
        byte gameMode, byte coins, byte anim = 0, byte midway = 0, byte exits = 0)
    {
        m.SetByte(GameMode, gameMode); m.SetByte(Coins, coins);
        m.SetByte(Anim, anim); m.SetByte(Midway, midway); m.SetByte(Exits, exits);
        c.Poll(m);
    }

    [Fact]
    public void SingleCoin_Bumps_AndAlerts()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0);    // baseline
        Poll(c, m, LevelMainMode, 1);    // +1 coin
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
    }

    [Fact]
    public void MultiCoinBurst_CountsFullDelta()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 10);
        Poll(c, m, LevelMainMode, 14);   // multi-coin block: +4 in one poll
        Assert.Equal(4, c.Value);
    }

    [Fact]
    public void WrapAt100_CountsAcrossTheWrap()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 99);
        Poll(c, m, LevelMainMode, 2);    // 99 -> (100-up) -> 2: +3
        Assert.Equal(3, c.Value);
    }

    [Fact]
    public void WrapBurst_AtTheLimit_Counts_OneOver_Resyncs()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 99);
        Poll(c, m, LevelMainMode, 14);   // 99 -> 14 across the wrap: candidate 15 == MaxWrapBurst
        Assert.Equal(15, c.Value);

        var c2 = new CoinCounter(); var m2 = new FakeSnesMemory();
        Poll(c2, m2, LevelMainMode, 99);
        Poll(c2, m2, LevelMainMode, 15); // candidate 16: one over the limit, treated as a resync
        Assert.Equal(0, c2.Value);
    }

    [Fact]
    public void DeathAndCollect_SamePoll_DeathRevertsFirst_ThenCollectCounts()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0);
        Poll(c, m, LevelMainMode, 4);            // +4 unbanked
        Poll(c, m, LevelMainMode, 7, anim: 9);   // same poll: die (revert to 0) then +3
        Assert.Equal(3, c.Value);
        Assert.True(c.ValueIsAlert);
    }

    [Fact]
    public void CollectAndBank_SamePoll_CollectLandsInTheBank()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0);
        Poll(c, m, LevelMainMode, 2, midway: 1); // same poll: +2 then midway banks
        Assert.Equal(2, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void LargeDrop_IsResync_NotAWrap()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 50);
        Poll(c, m, LevelMainMode, 20);   // hack-shop deduction: wrap math would say +70
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMainMode, 21);   // counting resumes from the new baseline
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void OffLevel_DoesNotCount()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, OverworldMode, 0);
        Poll(c, m, OverworldMode, 5);    // coin value moves while not in a level
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void GateFail_ClearsBaseline_NoBridgedDelta()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 10);
        Poll(c, m, OverworldMode, 10);   // leave level: baseline must clear
        Poll(c, m, LevelMainMode, 15);   // re-enter: re-baseline, don't bridge +5
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMainMode, 16);   // now counting again
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void DieBeforeBank_Reverts()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0);
        Poll(c, m, LevelMainMode, 3);            // +3 coins
        Assert.Equal(3, c.Value);
        Poll(c, m, LevelMainMode, 3, anim: 9);   // die -> unbanked coins revert
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Midway_Banks()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0);
        Poll(c, m, LevelMainMode, 2);              // +2
        Poll(c, m, LevelMainMode, 2, midway: 1);   // hit midway -> banked
        Assert.Equal(2, c.Value);
        Assert.False(c.ValueIsAlert);
        Poll(c, m, LevelMainMode, 2, midway: 1, anim: 9); // die after banking
        Assert.Equal(2, c.Value);                  // banked coins survive
    }

    [Fact]
    public void Exit_Banks()
    {
        var c = new CoinCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, exits: 5);
        Poll(c, m, LevelMainMode, 2, exits: 5);    // +2
        Poll(c, m, LevelMainMode, 2, exits: 6);    // $1F2E increments -> banked
        Assert.Equal(2, c.Value);
        Assert.False(c.ValueIsAlert);
    }
}
