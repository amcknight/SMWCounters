using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// StateHash must change whenever any state SaveState persists changes —
// including changes invisible to Value (a bank commit sets saved = total)
// — so LiveSplit's layout hash goes dirty and the layout actually saves.
public class StateHashTests
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

    [Fact]
    public void BankCommit_ChangesStateHash_WithoutChangingValue()
    {
        var c = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(c, m, LevelMainMode, 0, 0, 0);   // baseline
        Poll(c, m, LevelMainMode, 2, 0, 0);   // grab (total 1, saved 0)
        int before = ((ISmwCounter)c).StateHash;
        int valueBefore = c.Value;
        Poll(c, m, LevelMainMode, 0, 1, 0);   // midway -> bank (saved = total)
        Assert.Equal(valueBefore, c.Value);   // Value did not move...
        Assert.NotEqual(before, ((ISmwCounter)c).StateHash); // ...but the hash did
    }

    [Fact]
    public void UnbankedAndBanked_SameTotal_HashDifferently()
    {
        var unbanked = new PowerupCounter(); var m = new FakeSnesMemory();
        Poll(unbanked, m, LevelMainMode, 0, 0, 0);
        Poll(unbanked, m, LevelMainMode, 2, 0, 0);   // total 1, saved 0

        var banked = new PowerupCounter();
        banked.SetValue(1);                          // total 1, saved 1

        Assert.Equal(unbanked.Value, banked.Value);
        Assert.NotEqual(((ISmwCounter)unbanked).StateHash, ((ISmwCounter)banked).StateHash);
    }

    [Fact]
    public void MoonDedupeModeFlip_ChangesStateHash()
    {
        var c = new MoonCounter();
        int before = ((ISmwCounter)c).StateHash;
        c.DedupeMode = MoonDedupeMode.PerLevel;
        Assert.NotEqual(before, ((ISmwCounter)c).StateHash);
    }

    [Fact]
    public void DeathValue_DrivesStateHash()
    {
        var c = new DeathCounter();
        int before = ((ISmwCounter)c).StateHash;
        c.SetValue(3);
        Assert.NotEqual(before, ((ISmwCounter)c).StateHash);
    }

    [Fact]
    public void KillModeFlip_ChangesStateHash()
    {
        var c = new KillCounter();
        int before = ((ISmwCounter)c).StateHash;
        c.Mode = KillCountMode.Destruction;
        Assert.NotEqual(before, ((ISmwCounter)c).StateHash);
    }
}
