using System.Xml;

using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// The rule under test: "collect at least 5 Dragon Coins in levels that have
// at least 5". One count the moment the fifth coin is taken (the 1-up), and
// never again in that level — a sixth coin adds nothing. The game keeps the
// coins across a death once the fifth is taken, so the count is permanent
// and never gold.
public class DragonCoinCounterTests
{
    private const int GameMode = 0x0100;
    private const int YoshiCoins = 0x1420;
    private const int Anim = 0x0071;

    private const byte LevelMain = 0x14;
    private const byte Overworld = 0x0E;
    private const byte Dying = 9;

    private static void Poll(DragonCoinCounter c, FakeSnesMemory m, byte gameMode, byte coins, byte anim = 0)
    {
        m.SetByte(GameMode, gameMode);
        m.SetByte(YoshiCoins, coins);
        m.SetByte(Anim, anim);
        c.Poll(m);
    }

    // Enter a level with the byte at 0 and take coins one poll at a time.
    private static void CollectInLevel(DragonCoinCounter c, FakeSnesMemory m, byte count)
    {
        for (byte i = 0; i <= count; i++) { Poll(c, m, LevelMain, i); }
    }

    [Fact]
    public void FifthCoin_CountsOnce()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 4);
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMain, 5);
        Assert.Equal(1, c.Value);
        Poll(c, m, LevelMain, 5);   // held
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void SixthAndSeventhCoin_DoNotCount()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 5);
        Poll(c, m, LevelMain, 6);
        Poll(c, m, LevelMain, 7);
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void FifthCoin_IsNeverAlert()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 5);
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void DeathAfterFifthCoin_KeepsTheCount()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 5);
        Poll(c, m, LevelMain, 5, anim: Dying);
        Poll(c, m, Overworld, 0);
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void DeathBeforeFifth_ThenFiveOnReentry_CountsOnce()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 3);
        Poll(c, m, LevelMain, 3, anim: Dying);
        Poll(c, m, Overworld, 0);          // the level reload zeroes the byte
        CollectInLevel(c, m, 5);
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void TwoLevels_CountTwo()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 5);
        Poll(c, m, Overworld, 0);
        CollectInLevel(c, m, 5);
        Assert.Equal(2, c.Value);
    }

    [Fact]
    public void ThreeToFiveInOnePoll_CountsOnce()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 3);
        Poll(c, m, LevelMain, 5);   // two coins inside one poll gap
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void NotInLevel_ByteJumpingPastFive_DoesNotCount()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, Overworld, 0);
        Poll(c, m, Overworld, 5);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void EnteringLevel_WithByteAlreadyAtFive_OnlyBaselines()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, Overworld, 2);     // stale value, ignored
        Poll(c, m, LevelMain, 5);     // first in-level sample is the baseline
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMain, 6);     // already past five: nothing new
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void SublevelTransition_ResumesFromTheCarriedCount()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 3);
        Poll(c, m, 0x0F, 3);          // pipe: fade out of level-main
        Poll(c, m, LevelMain, 3);     // sublevel; the byte carried over
        Poll(c, m, LevelMain, 4);
        Poll(c, m, LevelMain, 5);
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void Detach_ClearsTheBaseline()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 4);
        m.Attached = false;
        c.Poll(m);
        m.Attached = true;
        Poll(c, m, LevelMain, 5);     // reattach sample only baselines
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void UnreadableByte_ClearsTheBaseline()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 4);
        var blank = new FakeSnesMemory();
        blank.SetByte(GameMode, LevelMain);   // mode readable, coin byte not
        c.Poll(blank);
        Poll(c, m, LevelMain, 5);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void HasNoDiscardOnDeathToggle()
    {
        Assert.False(new DragonCoinCounter() is IBankToggleCounter);
    }

    [Fact]
    public void Reset_ClearsValueAndBaseline()
    {
        var c = new DragonCoinCounter();
        var m = new FakeSnesMemory();
        CollectInLevel(c, m, 4);
        c.Reset();
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMain, 5);     // post-reset sample only baselines
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void SetValue_OverwritesTheCount()
    {
        var c = new DragonCoinCounter();
        c.SetValue(7);
        Assert.Equal(7, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Value_SurvivesRoundTrip_UnderAStableElementName()
    {
        var c = new DragonCoinCounter();
        c.SetValue(3);

        var doc = new XmlDocument();
        XmlElement el = doc.CreateElement("state");
        c.SaveState(doc, el);
        Assert.Equal("3", el["DragonCoins"].InnerText);

        var restored = new DragonCoinCounter();
        restored.LoadState(el);
        Assert.Equal(3, restored.Value);
        Assert.Equal(((ISmwCounter)c).StateHash, ((ISmwCounter)restored).StateHash);
    }

    [Fact]
    public void Id_IsStable()
    {
        ISmwCounter c = new DragonCoinCounter();
        Assert.Equal("dragoncoins", c.Id);
    }
}
