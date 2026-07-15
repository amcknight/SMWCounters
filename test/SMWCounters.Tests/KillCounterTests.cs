using System.Xml;

using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class KillCounterTests
{
    private const int GameMode = 0x0100, StatusBase = 0x14C8, SpriteBase = 0x009E, CoinCount = 0x0DBF;
    private const byte Level = 0x14, Alive = 0x08, Carryable = 0x09, Kicked = 0x0A,
                       Mouth = 0x07, Spinjump = 0x04, Falling = 0x02, TapeCoin = 0x06;
    // Sprite IDs used in tests (from the spec's evidence table).
    private const byte Galoomba = 0x0F, GreenKoopa = 0x04, PSwitch = 0x3E,
                       GoalTape = 0x7B, MovingCoin = 0x21, BulletBill = 0x1C,
                       Springboard = 0x2F;

    // Drive sprite slot 0 only (other slots unset => reads fail => cleared/skipped).
    // Every poll must supply a sprite ID too — the counter reads $9E per slot.
    private static void PollSlot0(KillCounter c, FakeSnesMemory m, byte status,
                                  byte sprite = Galoomba, byte gameMode = Level)
    {
        m.SetByte(GameMode, gameMode);
        m.SetByte(StatusBase, status);
        m.SetByte(SpriteBase, sprite);
        c.Poll(m);
    }

    private static int Kills(KillCounter c)
    {
        c.Mode = KillCountMode.Kills;
        return c.Value;
    }

    private static int Destruction(KillCounter c)
    {
        c.Mode = KillCountMode.Destruction;
        return c.Value;
    }

    [Fact]
    public void LiveToDead_CountsOnceInBothTallies_AndDoesNotRecount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, Spinjump);   // 08 -> 04 : kill + destruction
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
        PollSlot0(c, m, Spinjump);   // still 04 : no recount
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void WithinDeadSetShuffle_DoesNotDoubleCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, Spinjump);   // 08 -> 04 : counts
        PollSlot0(c, m, Falling);    // 04 -> 02 : within dead-set, silent
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void Offscreen_DoesNotCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, 0x00);       // 08 -> 00 : despawn
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }

    [Fact]
    public void EntryFromEmpty_DoesNotCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, 0x00);
        PollSlot0(c, m, Falling);    // 00 -> 02 : spawned-into-dead
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }

    [Fact]
    public void ExcludedId_DeadEntry_DestructionOnly()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Carryable, PSwitch);
        PollSlot0(c, m, Spinjump, PSwitch);  // P-switch poof: 09 -> 04
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void GoalTapeSelfConversion_CountsNothing()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, GoalTape);
        PollSlot0(c, m, TapeCoin, GoalTape); // #7B 08 -> 06
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }

    [Fact]
    public void CreatureTapeCoin_CountsInBoth()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, BulletBill);
        PollSlot0(c, m, TapeCoin, BulletBill); // bullet bill converted at goal
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void BareShellKill_DestructionOnly_LivingKoopaKill_Both()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Kicked, GreenKoopa);
        PollSlot0(c, m, Falling, GreenKoopa);  // bare shell 0A -> 02: koopa rule excludes
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));

        var c2 = new KillCounter(); var m2 = new FakeSnesMemory();
        PollSlot0(c2, m2, Alive, GreenKoopa);
        PollSlot0(c2, m2, Falling, GreenKoopa); // living koopa 08 -> 02: counts
        Assert.Equal(1, Kills(c2));
        Assert.Equal(1, Destruction(c2));
    }

    [Fact]
    public void FlippedGaloombaFinish_CountsInBoth()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Carryable, Galoomba);
        PollSlot0(c, m, Spinjump, Galoomba);   // 09 -> 04, ID outside 04-07
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void MultipleSlotsDyingInOneTick_EachCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        m.SetByte(GameMode, Level);
        m.SetByte(StatusBase + 0, Alive); m.SetByte(SpriteBase + 0, Galoomba);
        m.SetByte(StatusBase + 1, Alive); m.SetByte(SpriteBase + 1, BulletBill);
        c.Poll(m);
        m.SetByte(StatusBase + 0, Spinjump); m.SetByte(StatusBase + 1, Falling);
        c.Poll(m);
        Assert.Equal(2, Kills(c));
    }

    [Fact]
    public void GameModeGate_ClearsEdges_NoBridgedKill()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, Galoomba, Level);
        PollSlot0(c, m, Alive, Galoomba, 0x00);   // gate off: clears edges
        PollSlot0(c, m, Spinjump, Galoomba, Level); // no prior sample: silent
        Assert.Equal(0, Kills(c));
    }

    [Fact]
    public void Detach_ClearsEdges_NoBridgedKill()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        m.Attached = false; c.Poll(m);
        m.Attached = true;
        PollSlot0(c, m, Spinjump);
        Assert.Equal(0, Kills(c));
    }

    [Fact]
    public void DualTallies_ModeSelectsDisplay_SetValueWritesSelectedOnly()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Carryable, PSwitch);
        PollSlot0(c, m, Spinjump, PSwitch);  // kills 0, destruction 1
        c.Mode = KillCountMode.Kills;
        c.SetValue(10);                      // writes kills only
        Assert.Equal(10, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void Reset_ZeroesBothTallies_AndClearsEdges()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, Spinjump);
        c.Reset();
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
        PollSlot0(c, m, Spinjump);           // no prior sample after reset
        Assert.Equal(0, Kills(c));
    }

    [Fact]
    public void SaveLoad_RoundTripsBothTalliesAndMode()
    {
        var c = new KillCounter();
        c.Mode = KillCountMode.Kills; c.SetValue(7);
        c.Mode = KillCountMode.Destruction; c.SetValue(9);
        var doc = new XmlDocument();
        var parent = doc.CreateElement("kills");
        c.SaveState(doc, parent);

        // Locks the stable serialization element names.
        Assert.Equal("7", parent["Kills"].InnerText);
        Assert.Equal("9", parent["Destruction"].InnerText);
        Assert.Equal("Destruction", parent["KillMode"].InnerText);

        var restored = new KillCounter();
        restored.LoadState(parent);
        Assert.Equal(KillCountMode.Destruction, restored.Mode);
        Assert.Equal(9, restored.Value);
        restored.Mode = KillCountMode.Kills;
        Assert.Equal(7, restored.Value);
    }

    [Fact]
    public void LoadV1Document_DefaultsDestructionZeroModeKills()
    {
        var doc = new XmlDocument();
        var parent = doc.CreateElement("kills");
        var kills = doc.CreateElement("Kills");
        kills.InnerText = "5";
        parent.AppendChild(kills);

        var c = new KillCounter();
        c.LoadState(parent);
        Assert.Equal(KillCountMode.Kills, c.Mode);
        Assert.Equal(5, c.Value);
        c.Mode = KillCountMode.Destruction;
        Assert.Equal(0, c.Value);
    }
}
