using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class PowerupCounterTests
{
    private const int Level = 0x1935, Anim = 0x0071, Midway = 0x13CE, Exits = 0x1F2E;

    private static void Poll(PowerupCounter c, FakeSnesMemory m,
        byte level, byte anim, byte midway, byte exits)
    {
        m.SetByte(Level, level); m.SetByte(Anim, anim);
        m.SetByte(Midway, midway); m.SetByte(Exits, exits);
        c.Poll(m);
    }

    [Fact]
    public void CollectInLevel_Bumps_AndAlerts()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 1, 0, 0, 0);   // in level, baseline
        Poll(c, m, 1, 2, 0, 0);   // grab mushroom (anim 2)
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
    }

    [Fact]
    public void CollectOffLevel_DoesNotCount()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 0, 0, 0, 0);
        Poll(c, m, 0, 3, 0, 0);   // feather anim while not in level
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void DieBeforeSave_Discards()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 1, 0, 0, 0);
        Poll(c, m, 1, 2, 0, 0);   // grab (total 1)
        Poll(c, m, 1, 9, 0, 0);   // die
        Assert.Equal(0, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Midway_Banks()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 1, 0, 0, 0);
        Poll(c, m, 1, 4, 0, 0);   // grab flower (total 1)
        Poll(c, m, 1, 0, 1, 0);   // hit midway -> banked
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void Exit_Banks()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 1, 0, 0, 5);   // baseline exits=5
        Poll(c, m, 1, 2, 0, 5);   // grab (total 1)
        Poll(c, m, 1, 0, 0, 6);   // $1F2E increments -> banked
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
    }

    [Fact]
    public void LowPercentLoop()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, 1, 0, 0, 0);
        Poll(c, m, 1, 2, 0, 0); Assert.True(c.ValueIsAlert);   // grab
        Poll(c, m, 1, 9, 0, 0); Assert.False(c.ValueIsAlert);  // die -> discard
        Assert.Equal(0, c.Value);
        Poll(c, m, 1, 2, 0, 0); Assert.Equal(1, c.Value);      // grab again
        Poll(c, m, 1, 0, 1, 0); Assert.False(c.ValueIsAlert);  // reach midway -> banked
        Assert.Equal(1, c.Value);
    }
}
