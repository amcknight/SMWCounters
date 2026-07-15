# Kills / Destruction v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rework `KillCounter` into a dual-tally Kills/Destruction counter (evidence-driven creature filter, Yoshi mouth rules, fireball-coin pipeline) with a mode radio in settings and DebugLogger upgrades for future validation.

**Architecture:** One `KillCounter` maintains two always-computed tallies (`kills`, `destruction`); a `Mode` property selects which one `Value` displays. Per-slot edge detection grows from status-only to status + sprite number + mouth-entry classification + pending-coin flag. The settings radio follows the existing `BuildExtras` pattern (see `MoonCounter`'s checkbox). Spec: `docs/superpowers/specs/2026-07-14-kill-destruction-v2-design.md`.

**Tech Stack:** C# (.NET Framework, LiveSplit component), xUnit + `FakeSnesMemory` for tests, WinForms for settings UI.

## Global Constraints

- `Id` must stay `"kills"` — stable serialization key, never change.
- Dead-set is exactly `{0x02..0x06}`. Status `0x07` (Yoshi's mouth) is governed only by the mouth rules (E2-E5 in the spec).
- Not-alive sprite-ID list is exactly `{0x1B, 0x21, 0x2F, 0x3E, 0x4B, 0x53, 0x78, 0x7B, 0xC8}` (evidence-driven; do not add speculative IDs).
- Koopa origin rule: sprite IDs `0x04-0x07` pass the creature filter only when the origin status is `0x08`.
- Design priority: when in doubt, err toward NOT counting a Kill.
- In-level gate: `$0100 == 0x14`; detach/gate-off/reset/load clear all per-slot edge state (no bridged events).
- Both tallies always update; the radio/mode is a display selector only. `SetValue` writes the selected tally only; `Reset` clears both.
- Persistence elements: `<Kills>`, `<Destruction>`, `<KillMode>` (enum name string, like MoonCounter's `DedupeMode`). A v1 document (only `<Kills>`) must load with destruction 0, mode Kills.
- Build: `dotnet build src/SMWCounters/SMWCounters.csproj -c Release`. Test: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`.
- The Yoshi **insta-eat rule is NOT in this plan** — it is research-gated (spec section "Yoshi insta-eat coverage"). Task 5 ships the logger upgrades that enable the research session; the rule lands in a follow-up plan once the WRAM signal is confirmed.

---

### Task 1: Dual tallies, creature filter, E1 dead-set entry

**Files:**
- Modify: `src/SMWCounters/Counters/KillCounter.cs` (full rewrite of the file)
- Test: `test/SMWCounters.Tests/KillCounterTests.cs` (full rewrite of the file)

**Interfaces:**
- Consumes: `ISmwCounter`, `ISnesMemory.ReadWramByte(int, out byte)` / `IsAttached`, `PreviousByte` (`.HasPrevious`, `.Value`, `.Set(byte)`, `.Clear()`), `LiveSplit.UI.SettingsHelper.CreateSetting` / `ParseInt`, `IconLoader.Load`, `SMWCounters.Tests.FakeSnesMemory`.
- Produces (later tasks rely on these exact names): `internal enum KillCountMode { Kills, Destruction }`; on `KillCounter`: `KillCountMode Mode { get; set; }`, private fields `int kills`, `int destruction`, arrays `prevStatus`/`prevSprite` (`PreviousByte[12]`), `void PollSlot(int i, byte status, byte sprite)`, `static bool IsCreature(byte sprite, byte originStatus)`, `static bool IsDead(byte status)`, `void ClearSlot(int i)`, `void ClearAll()`, consts `SpriteNumberBase = 0x009E`, `GoalTapeSprite = 0x7B`, `MovingCoinSprite = 0x21`.

- [ ] **Step 1: Write the failing tests**

Replace the entire contents of `test/SMWCounters.Tests/KillCounterTests.cs` with:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: FAIL to compile — `KillCountMode` does not exist.

- [ ] **Step 3: Write the implementation**

Replace the entire contents of `src/SMWCounters/Counters/KillCounter.cs` with:

```csharp
using System.Collections.Generic;
using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

internal enum KillCountMode { Kills, Destruction }

// Dual-tally counter over the SMW sprite tables ($14C8 status, $9E sprite
// number, 12 slots). Both tallies are always computed; Mode selects which one
// Value displays.
//
//   Kills:       creatures killed. Dead-set entries, live eats, and (later
//                task) collected fireball coins — all gated by the creature
//                filter below. Design priority: err toward NOT counting.
//   Destruction: anything destroyed. Same events without the filter, except
//                the goal tape's self-conversion (#7B -> 06) which destroys
//                nothing.
//
// Dead set = {02 killed/falling, 03 smushed, 04 spinjumped, 05 lava, 06
// goal-tape coin}. Status 07 (Yoshi's mouth) is deliberately NOT in the dead
// set: it is reversible (spit) and is handled by the mouth rules (E2-E5,
// later task).
//
// Creature filter (evidence-driven, from the 2026-07-14 log session — see the
// v2 spec): a sprite is "not alive" if its ID is on the observed-offender
// list, or if it is a koopa ID (04-07, shared by bare shells) dying from a
// carryable state instead of the normal routine (08).
internal sealed class KillCounter : ISmwCounter
{
    private const int GameModeOffset = 0x0100;
    private const byte LevelMainMode = 0x14;
    private const int SpriteStatusBase = 0x14C8;
    private const int SpriteNumberBase = 0x009E;
    private const int SlotCount = 12;
    private const byte GoalTapeSprite = 0x7B;
    private const byte MovingCoinSprite = 0x21;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.kill.png");

    // Observed to enter the dead set without being creatures:
    // 1B football, 21 moving coin, 2F springboard, 3E P-switch, 4B chuck rock,
    // 53 throw block, 78 1-up, 7B goal tape, C8 accordion block.
    private static readonly HashSet<byte> NotAlive = new()
    {
        0x1B, 0x21, 0x2F, 0x3E, 0x4B, 0x53, 0x78, 0x7B, 0xC8,
    };

    private readonly PreviousByte[] prevStatus;
    private readonly PreviousByte[] prevSprite;

    private int kills;
    private int destruction;

    public KillCounter()
    {
        prevStatus = new PreviousByte[SlotCount];
        prevSprite = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            prevStatus[i] = new PreviousByte();
            prevSprite[i] = new PreviousByte();
        }
    }

    public string Id => "kills";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Kills";

    // Display selector: both tallies are always maintained; the radio in
    // settings flips this to choose which one renders. Not a behavior switch.
    public KillCountMode Mode { get; set; } = KillCountMode.Kills;

    public int Value => Mode == KillCountMode.Kills ? kills : destruction;

    public bool ValueIsAlert => false;

    public void SetValue(int value)
    {
        if (Mode == KillCountMode.Kills) { kills = value; }
        else { destruction = value; }
    }

    public void Reset()
    {
        kills = 0;
        destruction = 0;
        ClearAll();
    }

    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            ClearAll();
            return;
        }

        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            ClearAll();
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (!memory.ReadWramByte(SpriteStatusBase + i, out byte status)
                || !memory.ReadWramByte(SpriteNumberBase + i, out byte sprite))
            {
                ClearSlot(i);
                continue;
            }
            PollSlot(i, status, sprite);
        }
    }

    private void PollSlot(int i, byte status, byte sprite)
    {
        PreviousByte pStat = prevStatus[i];
        PreviousByte pSpr = prevSprite[i];
        if (!pStat.HasPrevious || !pSpr.HasPrevious)
        {
            pStat.Set(status);
            pSpr.Set(sprite);
            return;
        }

        byte prevStat = pStat.Value;
        bool liveOrigin = prevStat != 0x00 && prevStat != 0x07 && !IsDead(prevStat);

        // E1: dead-set entry from a live origin.
        if (liveOrigin && IsDead(status))
        {
            if (IsCreature(sprite, prevStat)) { kills++; }
            if (!(sprite == GoalTapeSprite && status == 0x06)) { destruction++; }
        }

        pStat.Set(status);
        pSpr.Set(sprite);
    }

    // "Alive" gate for the Kills tally: observed-offender IDs never count, and
    // koopa IDs (04-07, shared with their bare shells) only count when the
    // origin was the normal routine (08) — a living koopa, not a shell.
    private static bool IsCreature(byte sprite, byte originStatus)
    {
        if (NotAlive.Contains(sprite)) { return false; }
        if (sprite >= 0x04 && sprite <= 0x07 && originStatus != 0x08) { return false; }
        return true;
    }

    private static bool IsDead(byte status) => status >= 0x02 && status <= 0x06;

    private void ClearSlot(int i)
    {
        prevStatus[i].Clear();
        prevSprite[i].Clear();
    }

    private void ClearAll()
    {
        for (int i = 0; i < SlotCount; i++) { ClearSlot(i); }
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Kills", kills);
        SettingsHelper.CreateSetting(doc, parent, "Destruction", destruction);
        SettingsHelper.CreateSetting(doc, parent, "KillMode", Mode.ToString());
    }

    public void LoadState(XmlElement parent)
    {
        kills = SettingsHelper.ParseInt(parent["Kills"], 0);
        destruction = SettingsHelper.ParseInt(parent["Destruction"], 0);

        XmlElement modeEl = parent["KillMode"];
        Mode = modeEl != null && System.Enum.TryParse(modeEl.InnerText, out KillCountMode mode)
            ? mode
            : KillCountMode.Kills;

        ClearAll();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: PASS — all `KillCounterTests` green, all other suites still green.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/KillCounterTests.cs
git commit -m "feat: dual-tally KillCounter with creature filter (kills/destruction)"
```

---

### Task 2: Yoshi mouth rules (E2-E5)

**Files:**
- Modify: `src/SMWCounters/Counters/KillCounter.cs` (extend per-slot state; replace `PollSlot`, `ClearSlot`)
- Test: `test/SMWCounters.Tests/KillCounterTests.cs` (append tests)

**Interfaces:**
- Consumes: everything Task 1 produced.
- Produces: `private enum MouthEntry : byte { None, Creature, Item }` and field `MouthEntry[] mouthEntry` (length `SlotCount`), used and cleared by Task 3's final `PollSlot` and by `ClearSlot`.

- [ ] **Step 1: Write the failing tests**

Append inside the `KillCounterTests` class:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: FAIL — the five new tests fail (mouth transitions currently do nothing); `CreatureEat_...` asserts 1 but gets 0.

- [ ] **Step 3: Extend the implementation**

In `src/SMWCounters/Counters/KillCounter.cs`:

Add inside the class, next to the other per-slot fields:

```csharp
    // Mouth-entry classification per slot: set when a sprite enters status 07,
    // cleared when it leaves (swallow, spit) or the slot is cleared. Decides
    // whether a swallow (07 -> 00) credits Destruction (item) or nothing
    // (creature already counted at the eat; unknown entry counts nothing —
    // conservative per the design priority).
    private enum MouthEntry : byte { None, Creature, Item }

    private readonly MouthEntry[] mouthEntry = new MouthEntry[SlotCount];
```

Replace the whole `PollSlot` method with:

```csharp
    private void PollSlot(int i, byte status, byte sprite)
    {
        PreviousByte pStat = prevStatus[i];
        PreviousByte pSpr = prevSprite[i];
        if (!pStat.HasPrevious || !pSpr.HasPrevious)
        {
            pStat.Set(status);
            pSpr.Set(sprite);
            return;
        }

        byte prevStat = pStat.Value;
        bool liveOrigin = prevStat != 0x00 && prevStat != 0x07 && !IsDead(prevStat);

        if (prevStat == 0x07 && status != 0x07)
        {
            // E4 (swallow) / E5 (spit): leaving the mouth either way clears
            // the recorded entry. Only a swallowed *item* adds (Destruction);
            // a creature was already counted when it was eaten.
            if (status == 0x00 && mouthEntry[i] == MouthEntry.Item) { destruction++; }
            mouthEntry[i] = MouthEntry.None;
        }
        else if (status == 0x07 && prevStat != 0x07 && liveOrigin)
        {
            // E2 (creature eaten) / E3 (item pickup). Classified by the
            // creature filter, NOT origin status: items can idle at 08 too
            // (springboard), so the ID list is the only reliable separator.
            if (IsCreature(sprite, prevStat))
            {
                kills++;
                destruction++;
                mouthEntry[i] = MouthEntry.Creature;
            }
            else
            {
                mouthEntry[i] = MouthEntry.Item;
            }
        }
        else if (liveOrigin && IsDead(status))
        {
            // E1: dead-set entry from a live origin.
            if (IsCreature(sprite, prevStat)) { kills++; }
            if (!(sprite == GoalTapeSprite && status == 0x06)) { destruction++; }
        }

        pStat.Set(status);
        pSpr.Set(sprite);
    }
```

Replace the whole `ClearSlot` method with:

```csharp
    private void ClearSlot(int i)
    {
        prevStatus[i].Clear();
        prevSprite[i].Clear();
        mouthEntry[i] = MouthEntry.None;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: PASS — all tests green, including all Task 1 tests.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/KillCounterTests.cs
git commit -m "feat: Yoshi mouth rules — eats, item pickups, swallows, spits (E2-E5)"
```

---

### Task 3: Fireball-coin pipeline (E6-E7)

**Files:**
- Modify: `src/SMWCounters/Counters/KillCounter.cs` (coin-count tracking; replace `Poll`, `PollSlot`, `ClearSlot`, `ClearAll`)
- Test: `test/SMWCounters.Tests/KillCounterTests.cs` (append tests)

**Interfaces:**
- Consumes: everything Tasks 1-2 produced.
- Produces: fields `bool[] pendingCoin` (length `SlotCount`), `PreviousByte prevCoins`, const `CoinCountOffset = 0x0DBF`; `PollSlot` signature becomes `PollSlot(int i, byte status, byte sprite, bool coinsChanged)`.

- [ ] **Step 1: Write the failing tests**

Append inside the `KillCounterTests` class:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: FAIL — `FireballConversion_...` asserts destruction 1 but gets 0 (ID flips are currently invisible).

- [ ] **Step 3: Extend the implementation**

In `src/SMWCounters/Counters/KillCounter.cs`:

Add next to the other consts:

```csharp
    private const int CoinCountOffset = 0x0DBF;
```

Add next to the other per-slot fields:

```csharp
    // Set when a creature was fireball-converted to a moving coin in this
    // slot; resolved by the coin's clean despawn (kill iff the coin counter
    // moved the same poll), cancelled by anything else (tongue grab, slot
    // reuse). Conservative: when unsure, no kill.
    private readonly bool[] pendingCoin = new bool[SlotCount];

    private readonly PreviousByte prevCoins = new();
```

Replace the whole `Poll` method with:

```csharp
    public void Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached)
        {
            ClearAll();
            return;
        }

        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            ClearAll();
            return;
        }

        // "Changed", not "increased": the counter wraps 99 -> 0 on the coin
        // that awards a life.
        bool coinsChanged = false;
        if (memory.ReadWramByte(CoinCountOffset, out byte coins))
        {
            coinsChanged = prevCoins.HasPrevious && coins != prevCoins.Value;
            prevCoins.Set(coins);
        }
        else
        {
            prevCoins.Clear();
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (!memory.ReadWramByte(SpriteStatusBase + i, out byte status)
                || !memory.ReadWramByte(SpriteNumberBase + i, out byte sprite))
            {
                ClearSlot(i);
                continue;
            }
            PollSlot(i, status, sprite, coinsChanged);
        }
    }
```

Replace the whole `PollSlot` method with:

```csharp
    private void PollSlot(int i, byte status, byte sprite, bool coinsChanged)
    {
        PreviousByte pStat = prevStatus[i];
        PreviousByte pSpr = prevSprite[i];
        if (!pStat.HasPrevious || !pSpr.HasPrevious)
        {
            pStat.Set(status);
            pSpr.Set(sprite);
            return;
        }

        byte prevStat = pStat.Value;
        byte prevSpr = pSpr.Value;
        bool liveOrigin = prevStat != 0x00 && prevStat != 0x07 && !IsDead(prevStat);

        // E6: fireball conversion — the sprite number flips to the moving coin
        // while the slot stays in the normal routine. Destruction immediately;
        // the *kill* waits for proof of collection (uncollected coins let the
        // enemy respawn).
        if (prevStat == 0x08 && status == 0x08
            && prevSpr != MovingCoinSprite && sprite == MovingCoinSprite)
        {
            if (IsCreature(prevSpr, prevStat))
            {
                destruction++;
                pendingCoin[i] = true;
            }
        }
        else if (pendingCoin[i])
        {
            // E7: a clean despawn of the still-coin slot counts as a collected
            // kill iff the coin counter moved this same poll. Anything else —
            // tongue grab, slot reuse, odd status — cancels the pending kill.
            if (sprite == MovingCoinSprite && status == 0x00)
            {
                if (coinsChanged) { kills++; }
                pendingCoin[i] = false;
            }
            else if (sprite != MovingCoinSprite || status != 0x08)
            {
                pendingCoin[i] = false;
            }
        }

        if (prevStat == 0x07 && status != 0x07)
        {
            // E4 (swallow) / E5 (spit): leaving the mouth either way clears
            // the recorded entry. Only a swallowed *item* adds (Destruction);
            // a creature was already counted when it was eaten.
            if (status == 0x00 && mouthEntry[i] == MouthEntry.Item) { destruction++; }
            mouthEntry[i] = MouthEntry.None;
        }
        else if (status == 0x07 && prevStat != 0x07 && liveOrigin)
        {
            // E2 (creature eaten) / E3 (item pickup). Classified by the
            // creature filter, NOT origin status: items can idle at 08 too
            // (springboard), so the ID list is the only reliable separator.
            if (IsCreature(sprite, prevStat))
            {
                kills++;
                destruction++;
                mouthEntry[i] = MouthEntry.Creature;
            }
            else
            {
                mouthEntry[i] = MouthEntry.Item;
            }
        }
        else if (liveOrigin && IsDead(status))
        {
            // E1: dead-set entry from a live origin.
            if (IsCreature(sprite, prevStat)) { kills++; }
            if (!(sprite == GoalTapeSprite && status == 0x06)) { destruction++; }
        }

        pStat.Set(status);
        pSpr.Set(sprite);
    }
```

Replace the whole `ClearSlot` and `ClearAll` methods with:

```csharp
    private void ClearSlot(int i)
    {
        prevStatus[i].Clear();
        prevSprite[i].Clear();
        mouthEntry[i] = MouthEntry.None;
        pendingCoin[i] = false;
    }

    private void ClearAll()
    {
        for (int i = 0; i < SlotCount; i++) { ClearSlot(i); }
        prevCoins.Clear();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: PASS — all tests green, including all Task 1-2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/KillCounterTests.cs
git commit -m "feat: fireball-coin pipeline — destruction at conversion, kill on collection (E6-E7)"
```

---

### Task 4: Mode radio in settings

**Files:**
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs` (`BuildExtras`, around line 116)

**Interfaces:**
- Consumes: `KillCounter.Mode` / `KillCountMode` from Task 1; existing `extrasToolTip` field and the `(Control, Action)` return contract of `BuildExtras`.
- Produces: nothing new for later tasks — this is the last counter-facing change.

- [ ] **Step 1: Add the KillCounter branch to BuildExtras**

In `SmwCountersComponent.BuildExtras`, before the final `return (null, null);`, add:

```csharp
        if (counter is KillCounter kill)
        {
            var rdoKills = new RadioButton
            {
                Text = "Kills",
                AutoSize = true,
                Checked = kill.Mode == KillCountMode.Kills,
                Location = new Point(0, 2),
            };
            var rdoDestruction = new RadioButton
            {
                Text = "Destruction",
                AutoSize = true,
                Checked = kill.Mode == KillCountMode.Destruction,
                Location = new Point(52, 2),
            };
            rdoKills.CheckedChanged += (_, __) =>
            {
                if (rdoKills.Checked) { kill.Mode = KillCountMode.Kills; }
            };
            rdoDestruction.CheckedChanged += (_, __) =>
            {
                if (rdoDestruction.Checked) { kill.Mode = KillCountMode.Destruction; }
            };
            extrasToolTip.SetToolTip(rdoKills, "Count creatures killed (evidence-based not-alive sprite list applies).");
            extrasToolTip.SetToolTip(rdoDestruction, "Count anything destroyed: kills plus poofed/swallowed/converted objects.");
            var panel = new Panel { Width = 160, Height = 24, Padding = new Padding(0) };
            panel.Controls.Add(rdoKills);
            panel.Controls.Add(rdoDestruction);
            Action refresh = () =>
            {
                rdoKills.Checked = kill.Mode == KillCountMode.Kills;
                rdoDestruction.Checked = kill.Mode == KillCountMode.Destruction;
            };
            return (panel, refresh);
        }
```

Note: both tallies keep counting regardless of the radio; flipping it only changes which value displays. The settings value box shows the newly selected tally after the dialog refresh (reopen) — a live in-dialog swap is out of scope for v2.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/SMWCounters/SMWCounters.csproj -c Release`
Expected: Build succeeded, 0 errors. (If a running LiveSplit locks the Components copy, the copy step warns — that is fine.)

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/SMWCounters/UI/Components/SmwCountersComponent.cs
git commit -m "feat: Kills/Destruction display radio on the kills settings row"
```

---

### Task 5: DebugLogger upgrades (validation + Yoshi research tooling)

**Files:**
- Modify: `src/SMWCounters/Diagnostics/DebugLogger.cs`

**Interfaces:**
- Consumes: existing `DebugLogger` structure (`Poll`, `Idle`, `Write`, `Hex`, `prevStatus`, consts).
- Produces: new log line shapes — `SPR slot<n> id #xx->yy | status=ss mode=mm`, `coins=` in CTR context, `YOS ...` lines. Read by humans and future analysis scripts only; no code consumes them.

- [ ] **Step 1: Add sprite-number, coin, and Yoshi-candidate tracking**

In `src/SMWCounters/Diagnostics/DebugLogger.cs`:

Add to the consts block:

```csharp
    private const int CoinCount = 0x0DBF;        // fireball-coin collection correlation

    // Yoshi insta-eat research candidates (unverified — the whole point of
    // logging them is to confirm which signal is reliable before the counter
    // uses any of them; see the v2 spec, "Yoshi insta-eat coverage").
    private const int YoshiSwallowTimer = 0x18AC;
    private const int TongueTargetBase = 0x160E; // sprite misc table, per slot
    private const int MouthFlagBase = 0x1594;    // sprite misc table, per slot
    private const byte YoshiSpriteId = 0x35;
```

Add to the fields block:

```csharp
    private readonly PreviousByte[] prevSpriteNum;
    private readonly PreviousByte[] prevTongueTarget;
    private readonly PreviousByte[] prevMouthFlag;
    private readonly PreviousByte prevSwallowTimer = new();
```

In the constructor, after the existing `prevStatus` loop, add:

```csharp
        prevSpriteNum = new PreviousByte[SlotCount];
        prevTongueTarget = new PreviousByte[SlotCount];
        prevMouthFlag = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            prevSpriteNum[i] = new PreviousByte();
            prevTongueTarget[i] = new PreviousByte();
            prevMouthFlag[i] = new PreviousByte();
        }
```

In `Idle()`, extend the clearing loop to cover the new state (replace the method):

```csharp
    // Clear edge-detection state without closing the file (pause / detach).
    public void Idle()
    {
        lastValue.Clear();
        prevSwallowTimer.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            prevStatus[i].Clear();
            prevSpriteNum[i].Clear();
            prevTongueTarget[i].Clear();
            prevMouthFlag[i].Clear();
        }
    }
```

In `LogCounterChanges`, add `coins=` to the context string (replace the `ctx` assignment):

```csharp
                string ctx = $"mode={Hex(mem, GameMode)} inLvl={Hex(mem, InLevel)} "
                    + $"anim={Hex(mem, PlayerAnim)} fanfare={Hex(mem, Fanfare)} "
                    + $"io={Hex(mem, Io)} boss={Hex(mem, BossDefeat)} "
                    + $"exits={Hex(mem, ExitsSaved)} moon={Hex(mem, MoonByte)} "
                    + $"coins={Hex(mem, CoinCount)}";
```

Replace the whole `LogSpriteTransitions` method with:

```csharp
    private void LogSpriteTransitions(ISnesMemory mem)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            bool haveStatus = mem.ReadWramByte(SpriteStatusBase + i, out byte status);
            bool haveSprite = mem.ReadWramByte(SpriteNumberBase + i, out byte spriteNum);
            if (!haveStatus)
            {
                prevStatus[i].Clear();
                prevSpriteNum[i].Clear();
                continue;
            }

            if (prevStatus[i].HasPrevious && prevStatus[i].Value != status)
            {
                Write($"SPR slot{i} #{Hex(mem, SpriteNumberBase + i)} "
                    + $"{prevStatus[i].Value:X2}->{status:X2} | mode={Hex(mem, GameMode)}");
            }
            prevStatus[i].Set(status);

            // Sprite-number changes without a status change (fireball -> coin
            // conversions) were previously invisible; log them explicitly.
            if (haveSprite)
            {
                if (prevSpriteNum[i].HasPrevious && prevSpriteNum[i].Value != spriteNum
                    && status != 0x00)
                {
                    Write($"SPR slot{i} id #{prevSpriteNum[i].Value:X2}->#{spriteNum:X2} "
                        + $"| status={status:X2} mode={Hex(mem, GameMode)}");
                }
                prevSpriteNum[i].Set(spriteNum);
            }
            else
            {
                prevSpriteNum[i].Clear();
            }

            LogYoshiCandidates(mem, i, haveSprite ? spriteNum : (byte)0);
        }

        if (mem.ReadWramByte(YoshiSwallowTimer, out byte swallow))
        {
            if (prevSwallowTimer.HasPrevious && prevSwallowTimer.Value != swallow)
            {
                Write($"YOS 18AC {prevSwallowTimer.Value:X2}->{swallow:X2}");
            }
            prevSwallowTimer.Set(swallow);
        }
        else
        {
            prevSwallowTimer.Clear();
        }
    }

    // Research instrumentation: dump candidate Yoshi tongue/mouth bytes for
    // slots holding the Yoshi sprite, so a play session can establish which
    // signal reliably marks insta-eaten sprites (v2 spec, "Yoshi insta-eat
    // coverage"). Remove or repurpose once the signal is confirmed.
    private void LogYoshiCandidates(ISnesMemory mem, int slot, byte spriteNum)
    {
        if (spriteNum != YoshiSpriteId)
        {
            prevTongueTarget[slot].Clear();
            prevMouthFlag[slot].Clear();
            return;
        }

        if (mem.ReadWramByte(TongueTargetBase + slot, out byte tongue))
        {
            if (prevTongueTarget[slot].HasPrevious && prevTongueTarget[slot].Value != tongue)
            {
                Write($"YOS slot{slot} 160E {prevTongueTarget[slot].Value:X2}->{tongue:X2}");
            }
            prevTongueTarget[slot].Set(tongue);
        }

        if (mem.ReadWramByte(MouthFlagBase + slot, out byte mouth))
        {
            if (prevMouthFlag[slot].HasPrevious && prevMouthFlag[slot].Value != mouth)
            {
                Write($"YOS slot{slot} 1594 {prevMouthFlag[slot].Value:X2}->{mouth:X2}");
            }
            prevMouthFlag[slot].Set(mouth);
        }
    }
```

- [ ] **Step 2: Build and run the full test suite**

Run: `dotnet build src/SMWCounters/SMWCounters.csproj -c Release && dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: Build succeeded; all tests PASS (DebugLogger has no unit tests — it is best-effort I/O by design).

- [ ] **Step 3: Commit**

```bash
git add src/SMWCounters/Diagnostics/DebugLogger.cs
git commit -m "feat: DebugLogger logs sprite-id changes, coin count, Yoshi research candidates"
```

---

## Post-plan follow-ups (not tasks in this plan)

1. **Live validation session:** run LiveSplit with the rebuilt component, Kills row + debug log enabled; re-test the session's scenarios (P-switch, springboard eat/spit/swallow, shell loops, goal tape, fireball coin collected vs ignored, key eating). Expected: exclusion-list behavior matches the spec's E-table; any new offender IDs get appended to `NotAlive` with a log citation.
2. **Yoshi insta-eat research:** analyze the `YOS` lines from a Yoshi-heavy session; confirm which signal marks insta-eaten sprites; then write the follow-up plan implementing the insta-eat rule (spec section "Yoshi insta-eat coverage", test scenario 14).

## Self-Review

**Spec coverage:** dual tallies + display selector (Task 1), creature filter incl. koopa origin rule (Task 1), E1 + goal-tape self exclusion (Task 1), persistence incl. v1 compat (Task 1), E2-E5 mouth rules incl. no-recorded-entry conservatism (Task 2), E6-E7 fireball pipeline incl. tongue-grab cancel and slot-reuse cancel (Task 3), mode radio UI (Task 4), DebugLogger upgrades incl. Yoshi candidates (Task 5). Spec test scenarios 1-13 map to Tasks 1-3 test code; scenario 14 is explicitly research-gated (follow-up). Insta-eat rule deliberately absent per spec.

**Placeholder scan:** none — every step has complete code and exact commands.

**Type consistency:** `KillCountMode`, `MouthEntry`, `IsCreature(byte, byte)`, `PollSlot` signatures, `NotAlive`, `pendingCoin`, `prevCoins`, `ClearSlot`/`ClearAll` are used identically across Tasks 1-3; Task 4 consumes `kill.Mode`/`KillCountMode` as defined in Task 1; Task 5 touches only `DebugLogger` internals.
