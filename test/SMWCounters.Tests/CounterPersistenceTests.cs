using System.Xml;

using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// SaveState -> LoadState round-trips, driven through real poll sequences.
// Guards the persistence leg of the bank event: an unbanked total
// (total != saved, alert showing) must survive a save/load intact.
public class CounterPersistenceTests
{
    private const int GameMode = 0x0100, Anim = 0x0071, Midway = 0x13CE, Exits = 0x1F2E;
    private const byte LevelMainMode = 0x14;

    private static void Poll(PowerupCounter c, FakeSnesMemory m,
        byte gameMode, byte anim, byte midway, byte exits)
    {
        m.SetByte(GameMode, gameMode); m.SetByte(Anim, anim);
        m.SetByte(Midway, midway); m.SetByte(Exits, exits);
        c.Poll(m);
    }

    private static XmlElement RoundTrip(ISmwCounter save, ISmwCounter load)
    {
        var doc = new XmlDocument();
        XmlElement el = doc.CreateElement("state");
        save.SaveState(doc, el);
        load.LoadState(el);
        return el;
    }

    [Fact]
    public void UnbankedTotal_SurvivesRoundTrip()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 0);
        Poll(c, m, LevelMainMode, 2, 0, 0);   // total 1, saved 0 -> alert on

        var restored = new PowerupCounter();
        RoundTrip(c, restored);

        Assert.Equal(1, restored.Value);
        Assert.True(restored.ValueIsAlert);   // saved=0 came back too
        Assert.Equal(((ISmwCounter)c).StateHash, ((ISmwCounter)restored).StateHash);
    }

    [Fact]
    public void BankedTotal_SurvivesRoundTrip_WithoutAlert()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 0);
        Poll(c, m, LevelMainMode, 2, 0, 0);   // total 1, saved 0
        Poll(c, m, LevelMainMode, 0, 1, 0);   // midway -> banked (saved 1)

        var restored = new PowerupCounter();
        RoundTrip(c, restored);

        Assert.Equal(1, restored.Value);
        Assert.False(restored.ValueIsAlert);
        Assert.Equal(((ISmwCounter)c).StateHash, ((ISmwCounter)restored).StateHash);
    }

    [Fact]
    public void MoonDedupeMode_SurvivesRoundTrip()
    {
        var c = new MoonCounter { DedupeMode = MoonDedupeMode.PerLevel };
        c.SetValue(2);

        var restored = new MoonCounter();
        RoundTrip(c, restored);

        Assert.Equal(2, restored.Value);
        Assert.Equal(MoonDedupeMode.PerLevel, restored.DedupeMode);
        Assert.Equal(((ISmwCounter)c).StateHash, ((ISmwCounter)restored).StateHash);
    }
}
