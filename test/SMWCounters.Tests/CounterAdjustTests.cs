using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// The adjust hotkeys: a human correction routed through SetValue, so it
// clamps at zero, banks immediately, and touches only the displayed tally.
public class CounterAdjustTests
{
    [Fact] public void Apply_Negative_ClampsAtZero()
    { var c = new DeathCounter(); CounterAdjust.Apply(c, -1); Assert.Equal(0, c.Value); }

    [Fact] public void Apply_OnBankedCounter_BanksImmediately()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(0x0100, 0x14); m.SetByte(0x14C8, 0x08); m.SetByte(0x009E, 0x78); m.SetByte(0x16E1, 0x00); c.Poll(m);
      m.SetByte(0x14C8, 0x00); m.SetByte(0x16E1, 0x0D); c.Poll(m);
      Assert.True(c.ValueIsAlert);
      CounterAdjust.Apply(c, +1); Assert.Equal(2, c.Value); Assert.False(c.ValueIsAlert);
      CounterAdjust.Apply(c, -1); Assert.Equal(1, c.Value); Assert.False(c.ValueIsAlert); }

    [Fact] public void Apply_OnKills_AdjustsDisplayedModeOnly()
    { var c = new KillCounter { Mode = KillCountMode.Destruction }; CounterAdjust.Apply(c, +1);
      Assert.Equal(1, c.Value); c.Mode = KillCountMode.Kills; Assert.Equal(0, c.Value); }

    [Fact] public void Find_ById_And_UnknownId_ReturnsNull()
    { var list = new ISmwCounter[] { new DeathCounter(), new OneUpCounter() };
      Assert.IsType<OneUpCounter>(CounterAdjust.Find(list, "oneups")); Assert.Null(CounterAdjust.Find(list, "from-the-future")); }
}
