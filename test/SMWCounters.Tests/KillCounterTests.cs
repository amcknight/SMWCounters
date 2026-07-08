using System.Xml;

using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class KillCounterTests
{
    private const int GameMode = 0x0100, StatusBase = 0x14C8;
    private const byte Level = 0x14, Alive = 0x08, Spinjump = 0x04, Falling = 0x02;

    // Poll driving only sprite slot 0 (other slots left unset => read fails =>
    // that slot is cleared/skipped, so it never contributes a kill).
    private static void PollSlot0(KillCounter c, FakeSnesMemory m, byte status, byte gameMode = Level)
    {
        m.SetByte(GameMode, gameMode);
        m.SetByte(StatusBase, status);
        c.Poll(m);
    }

    [Fact]
    public void LiveToDead_CountsOnce_AndDoesNotRecount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, Spinjump);   // 08 -> 04 : kill
        Assert.Equal(1, c.Value);
        PollSlot0(c, m, Spinjump);   // still 04 : no recount
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void WithinDeadSetShuffle_DoesNotDoubleCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, Spinjump);   // 08 -> 04 : kill
        PollSlot0(c, m, Falling);    // 04 -> 02 : within dead-set, no count
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void Offscreen_DoesNotCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, 0x00);       // 08 -> 00 : despawn, no count
        Assert.Equal(0, c.Value);
    }

    [Theory]
    [InlineData((byte)0x02)]   // killed, falling off screen
    [InlineData((byte)0x03)]   // smushed
    [InlineData((byte)0x04)]   // spinjumped
    [InlineData((byte)0x05)]   // lava/mud
    [InlineData((byte)0x06)]   // goal-tape coin (controversially a kill; counted)
    [InlineData((byte)0x07)]   // eaten (inside Yoshi's mouth)
    public void EnteringDeadSet_Counts(byte dead)
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, dead);       // 08 -> 02..07 : counts
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void GoalTapePowerup_DoesNotCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, 0x0C);       // 08 -> 0C : outside 02..07, no count
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void EntryFromEmpty_DoesNotCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, 0x00);
        PollSlot0(c, m, Falling);    // 00 -> 02 : spawned-into-dead, no count
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void MultipleSlotsDyingInOneTick_EachCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        m.SetByte(GameMode, Level);
        m.SetByte(StatusBase + 0, Alive); m.SetByte(StatusBase + 1, Alive);
        c.Poll(m);
        m.SetByte(StatusBase + 0, Spinjump); m.SetByte(StatusBase + 1, Falling);
        c.Poll(m);
        Assert.Equal(2, c.Value);
    }

    [Fact]
    public void GameModeGate_ClearsEdges_NoBridgedKill()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, Level);
        PollSlot0(c, m, Alive, 0x00);   // not in level: gate off, clears edges
        PollSlot0(c, m, Spinjump, Level); // dead now, but prev was cleared: no count
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void Detach_ClearsEdges_NoBridgedKill()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        m.Attached = false; c.Poll(m);  // detach clears edges
        m.Attached = true;
        PollSlot0(c, m, Spinjump);       // prev cleared: no count
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void SaveLoad_RoundTripsValueUnderKillsKey()
    {
        var c = new KillCounter();
        c.SetValue(7);
        var doc = new XmlDocument();
        var parent = doc.CreateElement("kills");
        c.SaveState(doc, parent);

        // Locks the stable serialization element name "Kills".
        Assert.Equal("7", parent["Kills"].InnerText);

        var restored = new KillCounter();
        restored.LoadState(parent);
        Assert.Equal(7, restored.Value);
    }

    [Fact]
    public void Reset_ZeroesValueAndClearsEdges()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, Spinjump);       // 08 -> 04 : kill
        Assert.Equal(1, c.Value);

        c.Reset();
        Assert.Equal(0, c.Value);

        // Edges were cleared: the pre-reset live sample can't bridge to a kill.
        PollSlot0(c, m, Spinjump);       // no prior sample: sets prev, no count
        Assert.Equal(0, c.Value);
    }
}
