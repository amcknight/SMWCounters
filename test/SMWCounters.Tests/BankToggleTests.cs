using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class BankToggleTests
{
    // PowerupCounter collect = in-level ($0100==0x14, level-main mode) + playerAnimation
    // ($0071) shifting into {2,3,4}; die = $0071 -> 9.
    private static void Poll(PowerupCounter c, FakeSnesMemory m, byte gameMode, byte anim)
    {
        m.SetByte(0x0100, gameMode);
        m.SetByte(0x0071, anim);
        c.Poll(m);
    }

    [Fact]
    public void BankedOn_Default_CollectAlertsThenDeathReverts()
    {
        var c = new PowerupCounter();               // Banked defaults to true
        var m = new FakeSnesMemory();
        Poll(c, m, 0x14, 0);                           // baseline in-level
        Poll(c, m, 0x14, 2);                           // grab mushroom: total=1
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
        Poll(c, m, 0x14, 9);                           // die before banking: revert
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void BankedOff_CollectSticks_NoAlert_NoDeathRevert()
    {
        var c = new PowerupCounter { Banked = false };
        var m = new FakeSnesMemory();
        Poll(c, m, 0x14, 0);                           // baseline
        Poll(c, m, 0x14, 2);                           // grab mushroom: total=1
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);               // saved advanced with total
        Poll(c, m, 0x14, 9);                           // die: no revert
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void ToggleBankedOff_MidAlert_ReconcilesAndStopsAlertingAndSurvivesDeath()
    {
        var c = new PowerupCounter();               // Banked defaults to true
        var m = new FakeSnesMemory();
        Poll(c, m, 0x14, 0);                           // baseline in-level
        Poll(c, m, 0x14, 2);                           // grab mushroom: total=1
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);                // mid-alert: collected but not banked

        c.Banked = false;                           // toggle off while mid-alert
        Poll(c, m, 0x14, 2);                           // no collect, no death: just reconcile
        Assert.Equal(1, c.Value);                   // Value unchanged
        Assert.False(c.ValueIsAlert);                // gap closed: no longer alerting

        Poll(c, m, 0x14, 9);                           // die: must NOT revert now that off
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }
}
