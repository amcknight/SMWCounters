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
                       Springboard = 0x2F, PipeLakitu = 0x4B, ChuckRock = 0x48,
                       MessageBox = 0xB9, Yoshi = 0x35, Spiny = 0x13;
    private const int TongueTargetBase = 0x160E;
    private const byte NoTarget = 0xFF;

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

    [Fact]
    public void CreatureEat_CountsOnEat_SwallowAddsNothing()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, Galoomba);
        PollSlot0(c, m, Mouth, Galoomba);    // E2: 08 -> 07, creature
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
        PollSlot0(c, m, 0x00, Galoomba);     // E4: swallow, already counted
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void ItemEatSpitLoop_Silent_SwallowIsDestructionOnly()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        // Springboard idles at 08 (observed) — the eat looks status-identical
        // to eating a live enemy; only the ID list separates them.
        PollSlot0(c, m, Alive, Springboard);
        PollSlot0(c, m, Mouth, Springboard); // E3: item pickup
        PollSlot0(c, m, Alive, Springboard); // E5: spit back to 08
        PollSlot0(c, m, Mouth, Springboard); // E3 again
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
        PollSlot0(c, m, 0x00, Springboard);  // E4: swallowed item
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void BareShellEat_IsItemPickup_ByKoopaOriginRule()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Carryable, GreenKoopa); // bare shell idles at 09
        PollSlot0(c, m, Mouth, GreenKoopa);     // 09 -> 07: item, not creature
        PollSlot0(c, m, Carryable, GreenKoopa); // spit
        PollSlot0(c, m, Mouth, GreenKoopa);     // re-eat
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
        PollSlot0(c, m, 0x00, GreenKoopa);      // swallow
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void LiveShelledKoopaEat_CountsOnEat_SpitThenShellDeath_DestructionOnly()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, GreenKoopa);
        PollSlot0(c, m, Mouth, GreenKoopa);     // E2: living koopa eaten
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
        PollSlot0(c, m, Carryable, GreenKoopa); // E5: spit -> bare shell at 09
        PollSlot0(c, m, Falling, GreenKoopa);   // E1: shell dies from 09
        Assert.Equal(1, Kills(c));              // koopa rule: shell not a kill
        Assert.Equal(2, Destruction(c));
    }

    [Fact]
    public void SwallowWithoutRecordedEntry_CountsNothing()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Mouth, Galoomba);    // first sample IS 07: no entry seen
        PollSlot0(c, m, 0x00, Galoomba);     // swallow with entry unknown
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }

    // Fireball tests drive the coin counter ($0DBF) explicitly.
    private static void PollSlot0Coins(KillCounter c, FakeSnesMemory m, byte status,
                                       byte sprite, byte coins)
    {
        m.SetByte(CoinCount, coins);
        PollSlot0(c, m, status, sprite);
    }

    [Fact]
    public void FireballConversion_DestructionImmediately_KillOnCollectedCoin()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6: ID flips at 08
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);   // E7: despawn + coin gain
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void FireballCoinTimeout_NoKill()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 10);   // despawn, coins flat
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void ExcludedIdConversion_NoEventAtAll()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, PSwitch, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // not a creature: no E6
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);   // and no pending coin
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }

    [Fact]
    public void CoinTongueGrab_CancelsPendingKill_ClassifiesAsItemPickup()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6, pending
        PollSlot0Coins(c, m, Mouth, MovingCoin, coins: 10);  // tongue grab: cancel
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);   // swallow: item -> destruction
        Assert.Equal(0, Kills(c));
        Assert.Equal(2, Destruction(c));  // conversion + swallowed item
    }

    [Fact]
    public void SlotReuse_CancelsPendingCoin()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6, pending
        PollSlot0Coins(c, m, Alive, BulletBill, coins: 10);  // slot re-used
        PollSlot0Coins(c, m, 0x00, BulletBill, coins: 11);   // despawn + coin gain
        Assert.Equal(0, Kills(c));                           // stale pending gone
        Assert.Equal(1, Destruction(c));
    }

    // Pins every ID on the NotAlive exclusion list (KillCounter's NotAlive
    // HashSet): each one entering the dead set from a live origin counts
    // Destruction only, never Kills. 0x7B (goal tape) is included here with a
    // non-06 dead status — only its 06 self-conversion is fully excluded
    // (see GoalTapeSelfConversion_CountsNothing), and 0x04 exercises that
    // "only 06 is special" distinction directly.
    [Theory]
    [InlineData(0x1B)] // football
    [InlineData(0x21)] // moving coin
    [InlineData(0x2F)] // springboard
    [InlineData(0x3E)] // P-switch
    [InlineData(0x48)] // Diggin' Chuck's rock
    [InlineData(0x53)] // throw block
    [InlineData(0x78)] // 1-up
    [InlineData(0xB9)] // message box
    [InlineData(0xC8)] // accordion block
    [InlineData(0x7B)] // goal tape, non-06 dead status
    public void NotAliveTable_DeadEntry_DestructionOnly(byte spriteId)
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, spriteId);
        PollSlot0(c, m, Spinjump, spriteId);  // 08 -> 04: excluded ID, Destruction only
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    // Goal-burst items (1-up, accordion block) can enter the tape-coin status
    // (06) alongside a genuine goal-tape self-conversion. Unlike the tape
    // itself, they are not exempted from Destruction — only sprite #7B at
    // status 06 is.
    [Theory]
    [InlineData(0x78)] // 1-up
    [InlineData(0xC8)] // accordion block
    public void GoalBurstItem_TapeCoinEntry_DestructionOnly(byte spriteId)
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, spriteId);
        PollSlot0(c, m, TapeCoin, spriteId);  // 08 -> 06, non-goal-tape ID: destruction only
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    // 0x4B was blacklisted as "chuck rock" from the 2026-07-14 session, but the
    // 2026-07-16 session proved 0x4B is the pipe-dwelling Lakitu (it fireballs
    // into a coin and dies like a creature); the real rock is 0x48. Lakitu
    // kills must count in both tallies.
    [Fact]
    public void PipeLakituKill_CountsInBoth()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, PipeLakitu);
        PollSlot0(c, m, Spinjump, PipeLakitu);  // 08 -> 04: creature kill
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    // Message boxes convert 08 -> 06 with everything else when the goal tape
    // fires (observed 22:21:26 / 22:39:08, 2026-07-16 session) — destruction,
    // never a kill.
    [Fact]
    public void MessageBoxAtTape_DestructionOnly()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, MessageBox);
        PollSlot0(c, m, TapeCoin, MessageBox);  // 08 -> 06 at the tape
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    // The despawn edge and the coin-counter change routinely land one poll
    // apart (observed: spiny coin at 22:33:43 counted nothing while the
    // 22:32:04 one landed same-poll). The kill must survive the coin change
    // arriving a few polls late.
    [Fact]
    public void FireballCoin_CoinChangeOnePollAfterDespawn_StillCountsKill()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6, pending
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 10);   // despawn, coins not seen yet
        Assert.Equal(0, Kills(c));
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);   // coin lands one poll late
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void FireballCoin_CoinChangeWithinWindow_CountsKillOnce()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6, pending
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 10);   // despawn
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 10);   // coins still flat
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);   // 2 polls late: within window
        Assert.Equal(1, Kills(c));
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 12);   // later change: window consumed
        Assert.Equal(1, Kills(c));
    }

    [Fact]
    public void FireballCoin_CoinChangeAfterWindowExpires_NoKill()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
        PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6, pending
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 10);   // despawn opens the window
        for (int i = 0; i < 8; i++)
        {
            PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 10);  // window runs dry
        }
        PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);   // unrelated coin much later
        Assert.Equal(0, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    // Insta-eat polls drive slot 0 as the target and slot 1 as a mounted
    // Yoshi (#35 at status 08) whose $160E byte holds the tongue-target slot
    // index (FF = idle). Confirmed by the 2026-07-16 research session: six
    // insta-eats all latched $160E to the victim's slot, and the victim
    // despawned 08->00 on the same poll the byte reset to FF.
    private static void PollTongue(KillCounter c, FakeSnesMemory m, byte tongue,
                                   byte targetStatus, byte targetSprite)
    {
        m.SetByte(GameMode, Level);
        m.SetByte(StatusBase, targetStatus);
        m.SetByte(SpriteBase, targetSprite);
        m.SetByte(StatusBase + 1, Alive);
        m.SetByte(SpriteBase + 1, Yoshi);
        m.SetByte(TongueTargetBase + 1, tongue);
        c.Poll(m);
    }

    [Fact]
    public void InstaEat_TongueTargetedCreatureDespawns_CountsBoth()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollTongue(c, m, NoTarget, Alive, Spiny);
        PollTongue(c, m, 0x00, Alive, Spiny);     // tongue latches slot 0
        PollTongue(c, m, NoTarget, 0x00, Spiny);  // same poll: byte resets + despawn
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
        PollTongue(c, m, NoTarget, 0x00, Spiny);  // no recount
        Assert.Equal(1, Kills(c));
    }

    [Fact]
    public void InstaEat_ExcludedId_CountsNothing()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollTongue(c, m, NoTarget, Alive, PSwitch);
        PollTongue(c, m, 0x00, Alive, PSwitch);
        PollTongue(c, m, NoTarget, 0x00, PSwitch); // items are governed by mouth rules
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }

    [Fact]
    public void InstaEat_LingerExpired_PlainDespawnDoesNotCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollTongue(c, m, NoTarget, Alive, Spiny);
        PollTongue(c, m, 0x00, Alive, Spiny);      // tongue latches...
        for (int i = 0; i < 6; i++)
        {
            PollTongue(c, m, NoTarget, Alive, Spiny);  // ...but the target survives
        }
        PollTongue(c, m, NoTarget, 0x00, Spiny);   // later offscreen despawn
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }

    // A mouth-visible eat under an active tongue signal (living koopa: E2
    // fires at 08->07) must not double count when the swallow (07->00) lands
    // while the tongue linger is still warm.
    [Fact]
    public void InstaEat_MouthEatUnderTongueSignal_NoDoubleCount()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollTongue(c, m, NoTarget, Alive, Galoomba);
        PollTongue(c, m, 0x00, Alive, Galoomba);
        PollTongue(c, m, 0x00, Mouth, Galoomba);   // E2: kill counted at the eat
        Assert.Equal(1, Kills(c));
        PollTongue(c, m, NoTarget, 0x00, Galoomba); // swallow while linger warm
        Assert.Equal(1, Kills(c));
        Assert.Equal(1, Destruction(c));
    }

    [Fact]
    public void GameModeGate_MidMouth_ClearsRecordedItemEntry_SwallowCountsNothing()
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive, Springboard);
        PollSlot0(c, m, Mouth, Springboard);                   // E3: item pickup, entry recorded
        PollSlot0(c, m, Mouth, Springboard, gameMode: 0x00);   // gate off: ClearAll wipes the entry
        PollSlot0(c, m, Mouth, Springboard, gameMode: Level);  // back in-level, still 07: re-primes only
        PollSlot0(c, m, 0x00, Springboard);                    // swallow: entry unknown, counts nothing
        Assert.Equal(0, Kills(c));
        Assert.Equal(0, Destruction(c));
    }
}
