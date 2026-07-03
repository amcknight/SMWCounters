using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class ExitCounterTests
{
    // Full poll: fanfare ($0906), io ($1DFB), bossDefeat ($13C6),
    // exitsCompleted ($1F2E), death anim ($0071).
    private static void Poll(ExitCounter c, FakeSnesMemory m,
        byte fanfare, byte io, byte bossDefeat, byte exits, byte anim)
    {
        m.SetByte(0x0906, fanfare);
        m.SetByte(0x1DFB, io);
        m.SetByte(0x13C6, bossDefeat);
        m.SetByte(0x1F2E, exits);
        m.SetByte(0x0071, anim);
        c.Poll(m);
    }

    [Fact]
    public void Goal_Fanfare_CountsAndBanks()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 10, 0);   // baseline (exits already 10 from save)
        Poll(c, m, 1, 4, 0, 10, 0);   // fanfare 0->1, io=4 goal, boss alive: total=1
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
        Poll(c, m, 1, 4, 0, 11, 0);   // $1F2E increments: banked
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Orb_CountsOnce()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 0, 0);    // baseline
        Poll(c, m, 0, 3, 0, 0, 0);    // io -> 3 (orb), boss alive
        Poll(c, m, 0, 3, 0, 0, 0);    // held: no double count
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void Key_Counts()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 0, 0);
        Poll(c, m, 0, 7, 0, 0, 0);    // io -> 7 (keyhole)
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void Boss_CountsWhenFanfareWithBossDefeated()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 1, 0, 0);    // baseline, boss already defeated
        Poll(c, m, 1, 0, 1, 0, 0);    // fanfare 0->1 with bossDefeat != 0: boss exit
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void NoFinish_DoesNotCount()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 0, 0);
        Poll(c, m, 0, 1, 0, 0, 0);    // io wanders to a non-finish value
        Poll(c, m, 0, 0, 0, 0, 0);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void ExitModeShift_NoLongerCounts()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 0, 0);
        m.SetByte(0x0DD5, 4);         // old signal; must be ignored now
        c.Poll(m);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void AfterGoalDeath_DoesNotCount()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 10, 0);
        Poll(c, m, 1, 4, 0, 10, 0);   // goal: total=1 (alert)
        Poll(c, m, 1, 4, 0, 10, 9);   // death before $1F2E: revert
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void SaveLoadPopulate_NoPhantomBankOrCount()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 0, 0);    // baseline
        Poll(c, m, 0, 0, 0, 24, 0);   // $1F2E 0->24 populate, no finish
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Detach_ClearsBaseline_NoPhantomOnReattach()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 1, 4, 0, 10, 0);   // establish baselines mid-finish
        m.Attached = false; c.Poll(m);
        m.Attached = true;
        Poll(c, m, 1, 7, 0, 10, 0);   // first re-sample only re-baselines
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void LoadState_OldFormat_TreatsValueAsBanked()
    {
        var c = new ExitCounter();
        var doc = new System.Xml.XmlDocument();
        var el = doc.CreateElement("exits");
        var v = doc.CreateElement("Exits"); v.InnerText = "7"; el.AppendChild(v); // no ExitsSaved
        c.LoadState(el);
        Assert.Equal(7, c.Value);
        Assert.False(c.ValueIsAlert);   // saved defaulted to total
    }
}
