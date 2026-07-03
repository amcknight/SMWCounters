using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class ExitCounterTests
{
    private static void Poll(ExitCounter c, FakeSnesMemory m, byte exitMode)
    {
        m.SetByte(0x0DD5, exitMode);
        c.Poll(m);
    }

    [Fact]
    public void SaveLoad_DoesNotCount()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        // A save load leaves exitMode at 0 (it is an in-level end-of-level byte).
        Poll(c, m, 0);
        Poll(c, m, 0);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void ExitEvent_CountsOnce()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);   // in level, no exit yet
        Poll(c, m, 4);   // goal reached: exitMode shifts to a valid value
        Poll(c, m, 4);   // held: no double count
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void ExitMode128_IsIgnored()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);
        Poll(c, m, 128); // 128 is not a countable exit
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void TwoExits_CountTwice()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0);
        Poll(c, m, 4);   // exit 1
        Poll(c, m, 0);   // back to play
        Poll(c, m, 7);   // exit 2 (e.g. key)
        Assert.Equal(2, c.Value);
    }

    [Fact]
    public void Detach_ClearsBaseline_NoPhantomOnReattach()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 4);           // baseline established at 4
        m.Attached = false; c.Poll(m);
        m.Attached = true;
        Poll(c, m, 7);           // first post-reattach sample only re-baselines
        Assert.Equal(0, c.Value);
    }

    // Banked model (v0.2.0): total counts goal events, saved banks on $1F2E.
    private static void Poll(ExitCounter c, FakeSnesMemory m, byte exitMode, byte exitsCompleted, byte anim)
    {
        m.SetByte(0x0DD5, exitMode);
        m.SetByte(0x1F2E, exitsCompleted);
        m.SetByte(0x0071, anim);
        c.Poll(m);
    }

    [Fact]
    public void Goal_ThenCompletion_CountsAndBanks()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 10, 0);    // baseline (exits byte already 10 from save)
        Poll(c, m, 4, 10, 0);    // goal: total=1, not yet banked
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
        Poll(c, m, 0, 11, 0);    // $1F2E increments: banked
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void AfterGoalDeath_DoesNotCount()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 10, 0);
        Poll(c, m, 4, 10, 0);    // goal: total=1 (alert)
        Poll(c, m, 4, 10, 9);    // after-goal death before $1F2E: revert
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void SaveLoadPopulate_NoPhantomBankOrCount()
    {
        var c = new ExitCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0);     // baseline
        Poll(c, m, 0, 24, 0);    // save-load populates $1F2E 0->24, no goal
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
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
