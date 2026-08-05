# Banked Kills & Moons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend checkpoint banking (gold-until-lock, revert-on-death) to Kills/Destruction and Moons, make the "Discard on death" toggle a pure display selector over two always-tracked histories, and replace the per-row toggle checkbox with a settings column.

**Architecture:** A shared `DeathEdgeDetector` and an `IBankToggleCounter` interface join the existing `MidwayExitBankDetector`. `BankedCounter` gains a never-reverted `plain` tally; `Banked` only selects which history displays. `MoonCounter` becomes a `BankedCounter` subclass (dedupe deleted). `KillCounter` stays standalone but restructures its poll so death/bank detection runs at any game mode (bank signals fire after mode leaves `0x14` — log-cited). Settings storage moves from a `BankDisabled` id set to an explicit per-id `BankOnSave` map with per-counter defaults.

**Tech Stack:** C# / .NET Framework 4.8.1, WinForms (LiveSplit component), xunit.

**Spec:** `docs/superpowers/specs/2026-08-04-banked-kills-moons-design.md`

## Global Constraints

- Test command (all tasks): `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
- Filtered run: append `--filter "FullyQualifiedName~<TestClassName>"`
- The FULL suite must be green before every commit — these counters interlock.
- Committing directly to `master` is authorized for this repo (per CLAUDE.md).
- Production types are `internal`; the test project sees them via InternalsVisibleTo — never widen visibility to `public` for testability.
- Serialization element names are a stable on-disk API: `Moons`/`MoonsSaved`/`MoonsPlain`, `Kills`/`KillsSaved`/`KillsPlain`, `Destruction`/`DestructionSaved`/`DestructionPlain`, `KillMode`, settings `BankOnSave` (new) / `BankDisabled` (legacy, read-only).
- Toggle defaults: `moons` → OFF, every other toggle counter (`jumps`, `coins`, `powerups`, `kills`) → ON.
- Do NOT build the Release configuration until the final task: `dotnet build -c Release` copies the DLL to `C:\Apps\LiveSplit\Components` and requires LiveSplit to be closed (MSB3026/MSB3027 warnings mean the copy failed and a live test would run stale code).
- Windows shell: plain `cd`, never `cd /d`.

---

### Task 1: Extract `DeathEdgeDetector`

**Files:**
- Create: `src/SMWCounters/Counters/DeathEdgeDetector.cs`
- Modify: `src/SMWCounters/Counters/BankedCounter.cs`
- Create: `test/SMWCounters.Tests/DeathEdgeDetectorTests.cs`

**Interfaces:**
- Consumes: `PreviousByte` (existing), `ISnesMemory` (existing).
- Produces: `internal sealed class DeathEdgeDetector` with `bool Detect(ISnesMemory memory)` and `void Clear()`. Task 5 composes one in `KillCounter`; `BankedCounter` delegates to one from this task on.

- [ ] **Step 1: Write the failing tests**

Create `test/SMWCounters.Tests/DeathEdgeDetectorTests.cs`:

```csharp
using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

// The shared "$0071 rising edge to 9" death rule, extracted from
// BankedCounter so KillCounter (dual tally, not a BankedCounter subclass)
// can compose the identical discard edge.
public class DeathEdgeDetectorTests
{
    private const int Anim = 0x0071;
    private const byte Dying = 0x09;

    private static bool Poll(DeathEdgeDetector d, FakeSnesMemory m, byte anim)
    {
        m.SetByte(Anim, anim);
        return d.Detect(m);
    }

    [Fact]
    public void RisingEdgeToDying_FiresOnce_NotWhileHeld()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0));      // baseline
        Assert.True(Poll(d, m, Dying));   // 0 -> 9: the edge
        Assert.False(Poll(d, m, Dying));  // held at 9: no re-fire
    }

    [Fact]
    public void FirstSampleAlreadyDying_NoEdge()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, Dying));  // no previous sample: not an edge
    }

    [Fact]
    public void ReadFailure_ClearsBaseline_NoBridgedEdge()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0));
        Assert.False(d.Detect(new FakeSnesMemory()));  // $0071 unreadable: clears
        Assert.False(Poll(d, m, Dying));  // re-baseline only, no 0->9 bridge
    }

    [Fact]
    public void Clear_ResetsBaseline()
    {
        var d = new DeathEdgeDetector();
        var m = new FakeSnesMemory();
        Assert.False(Poll(d, m, 0));
        d.Clear();
        Assert.False(Poll(d, m, Dying));  // baseline gone: no edge
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter "FullyQualifiedName~DeathEdgeDetectorTests"`
Expected: build FAILS — `DeathEdgeDetector` does not exist.

- [ ] **Step 3: Create the detector**

Create `src/SMWCounters/Counters/DeathEdgeDetector.cs`:

```csharp
using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Shared "$0071 rising edge to 9" death rule — the discard edge for every
// banked history. Extracted from BankedCounter so KillCounter (dual tally,
// not a BankedCounter) composes the same rule instead of duplicating it.
internal sealed class DeathEdgeDetector
{
    private const int PlayerAnimationOffset = 0x0071;
    private const byte DyingValue = 0x09;

    private readonly PreviousByte previousAnim = new();

    public bool Detect(ISnesMemory memory)
    {
        if (!memory.ReadWramByte(PlayerAnimationOffset, out byte anim))
        {
            previousAnim.Clear();
            return false;
        }
        bool died = previousAnim.HasPrevious
            && previousAnim.Value != DyingValue && anim == DyingValue;
        previousAnim.Set(anim);
        return died;
    }

    public void Clear() => previousAnim.Clear();
}
```

- [ ] **Step 4: Delegate from `BankedCounter`**

In `src/SMWCounters/Counters/BankedCounter.cs`:

1. Delete the constants `PlayerAnimationOffset` and `DyingValue` and the field `previousDeathAnim`.
2. Add field: `private readonly DeathEdgeDetector deathEdge = new();`
3. Replace the `DetectDeath` body:

```csharp
// Default die-to-discard: rising edge of $0071 to the dying value.
protected virtual bool DetectDeath(ISnesMemory memory) => deathEdge.Detect(memory);
```

4. Replace every `previousDeathAnim.Clear();` with `deathEdge.Clear();` (three sites: `Reset()`, the `!memory.IsAttached` branch of `Poll`, and `LoadState`).

- [ ] **Step 5: Run the new tests, then the full suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS (behavior is identical; only the home of the rule moved).

- [ ] **Step 6: Commit**

```bash
git add src/SMWCounters/Counters/DeathEdgeDetector.cs src/SMWCounters/Counters/BankedCounter.cs test/SMWCounters.Tests/DeathEdgeDetectorTests.cs
git commit -m "refactor: extract the \$0071 death edge into DeathEdgeDetector"
```

---

### Task 2: Dual histories in `BankedCounter` — the toggle becomes a display selector

**Files:**
- Modify: `src/SMWCounters/Counters/BankedCounter.cs`
- Modify: `test/SMWCounters.Tests/BankToggleTests.cs`
- Modify: `test/SMWCounters.Tests/CounterPersistenceTests.cs`
- Modify: `test/SMWCounters.Tests/StateHashTests.cs`

**Interfaces:**
- Consumes: `DeathEdgeDetector` (Task 1).
- Produces: `BankedCounter` semantics every later task relies on: protected `int total, saved, plain`; `Value => Banked ? total : plain`; `ValueIsAlert => Banked && total != saved`; `StateHash` mixes all three; `SaveState` writes `<SaveName>`, `<SaveName>Saved`, `<SaveName>Plain`; missing elements default to `total` on load. `Banked` no longer changes what is tracked — only what is shown.

- [ ] **Step 1: Write the failing tests**

Append to `test/SMWCounters.Tests/BankToggleTests.cs` (the three existing tests stay — the new semantics keep them passing):

```csharp
[Fact]
public void ToggleBackOn_RevealsTheBankedHistory_IncludingPastReverts()
{
    var c = new PowerupCounter();               // Banked defaults to true
    var m = new FakeSnesMemory();
    Poll(c, m, 0x14, 0);                        // baseline in-level
    Poll(c, m, 0x14, 2);                        // grab: banked total=1, plain=1
    Poll(c, m, 0x14, 9);                        // die: banked reverts to 0, plain stays 1

    c.Banked = false;
    Assert.Equal(1, c.Value);                   // plain history: the grab still shows
    Assert.False(c.ValueIsAlert);

    c.Banked = true;
    Assert.Equal(0, c.Value);                   // banked history remembered the revert
    Assert.False(c.ValueIsAlert);               // total == saved == 0
}

[Fact]
public void BankedHistory_KeepsRunningWhileDisplayingPlain()
{
    var c = new PowerupCounter { Banked = false };
    var m = new FakeSnesMemory();
    Poll(c, m, 0x14, 0);
    Poll(c, m, 0x14, 2);                        // grab while plain view showing
    Poll(c, m, 0x14, 9);                        // die: banked history reverts underneath
    c.Banked = true;
    Assert.Equal(0, c.Value);                   // the revert happened even while hidden
}
```

Append to `test/SMWCounters.Tests/CounterPersistenceTests.cs`:

```csharp
[Fact]
public void PlainHistory_SurvivesRoundTrip()
{
    var c = new PowerupCounter(); var m = new FakeSnesMemory();
    Poll(c, m, LevelMainMode, 0, 0, 0);
    Poll(c, m, LevelMainMode, 2, 0, 0);   // total 1, plain 1, saved 0
    Poll(c, m, LevelMainMode, 9, 0, 0);   // die: total 0, plain 1

    var restored = new PowerupCounter();
    XmlElement el = RoundTrip(c, restored);
    Assert.Equal("1", el["PowerupsPlain"].InnerText);  // stable element name

    Assert.Equal(0, restored.Value);                   // banked view
    restored.Banked = false;
    Assert.Equal(1, restored.Value);                   // plain view
    Assert.Equal(((ISmwCounter)c).StateHash, ((ISmwCounter)restored).StateHash);
}

[Fact]
public void LegacyLayoutWithoutPlain_LoadsPlainAsTotal()
{
    var doc = new XmlDocument();
    XmlElement el = doc.CreateElement("state");
    XmlElement v = doc.CreateElement("Powerups");
    v.InnerText = "3";
    el.AppendChild(v);

    var c = new PowerupCounter();
    c.LoadState(el);
    Assert.Equal(3, c.Value);             // banked (saved also defaulted to 3)
    Assert.False(c.ValueIsAlert);
    c.Banked = false;
    Assert.Equal(3, c.Value);             // plain defaulted to total
}
```

Append to `test/SMWCounters.Tests/StateHashTests.cs`:

```csharp
[Fact]
public void PlainHistory_DrivesStateHash_EvenWhenBankedValueMatches()
{
    var a = new PowerupCounter(); var m = new FakeSnesMemory();
    Poll(a, m, LevelMainMode, 0, 0, 0);
    Poll(a, m, LevelMainMode, 2, 0, 0);   // grab
    Poll(a, m, LevelMainMode, 9, 0, 0);   // die: total 0/saved 0/plain 1

    var b = new PowerupCounter();          // untouched: 0/0/0
    Assert.Equal(a.Value, b.Value);        // both display 0 (banked view)
    Assert.NotEqual(((ISmwCounter)a).StateHash, ((ISmwCounter)b).StateHash);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter "FullyQualifiedName~BankToggleTests|FullyQualifiedName~CounterPersistenceTests|FullyQualifiedName~StateHashTests"`
Expected: new tests FAIL (`Banked` setter currently gates tracking, no `plain`, no `PowerupsPlain` element); existing ones still pass.

- [ ] **Step 3: Implement dual histories**

In `src/SMWCounters/Counters/BankedCounter.cs`:

1. Replace the class doc comment:

```csharp
// Shared "collect, then bank or discard-on-death" counter tracking BOTH
// histories every poll:
//   banked:  collect => total += delta; die => total = saved (DeathEdge);
//            bank => saved = total (subclass DetectBank)
//   plain:   collect => plain += delta; never reverted
// Banked is a pure display selector — it never changes what is tracked:
//   Value       = Banked ? total : plain
//   ValueIsAlert= Banked && total != saved   (plain view never alerts)
// Flipping the "Discard on death" toggle mid-run therefore snaps the shown
// value to what it would have been had the setting been that way all along.
```

2. Add the field and change members:

```csharp
protected int total;
protected int saved;
protected int plain;   // the never-reverted "discard off" history

// Display selector (see class comment). Driven per-poll from the
// "Discard on death" setting for counters with HasBankToggle.
public bool Banked { get; set; } = true;

public int Value => Banked ? total : plain;
public bool ValueIsAlert => Banked && total != saved;

// Everything SaveState persists must feed the hash — including values the
// current display hides (saved, and the non-displayed history).
public int StateHash => (total * 397 ^ saved) * 397 ^ plain;
```

3. `Reset()`: add `plain = 0;` next to `total = 0; saved = 0;`.
4. `SetValue(int value)`: add `plain = value;`.
5. `Poll`: delete the line `if (!Banked && total != saved) { saved = total; }` and change the collect line:

```csharp
if (DetectDeath(memory)) { total = saved; }
int delta = DetectCollectDelta(memory);
if (delta > 0) { total += delta; plain += delta; }
if (DetectBank(memory)) { saved = total; }
```

6. `SaveState`: add `SettingsHelper.CreateSetting(doc, parent, SaveName + "Plain", plain);`.
7. `LoadState`: after the `saved` line add `plain = SettingsHelper.ParseInt(parent[SaveName + "Plain"], total);`.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS. Note: the three pre-existing `BankToggleTests` pass unchanged — the observable `Banked = false` behavior (value sticks, no alert, survives death) is identical; only the mechanism changed.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/BankedCounter.cs test/SMWCounters.Tests/BankToggleTests.cs test/SMWCounters.Tests/CounterPersistenceTests.cs test/SMWCounters.Tests/StateHashTests.cs
git commit -m "feat: banked counters track both histories; the toggle only selects the display"
```

---

### Task 3: `IBankToggleCounter` + component sync via the interface

**Files:**
- Create: `src/SMWCounters/Counters/IBankToggleCounter.cs`
- Modify: `src/SMWCounters/Counters/BankedCounter.cs` (implements it)
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs:257` (sync via interface)
- Modify: `test/SMWCounters.Tests/BankToggleTests.cs`

**Interfaces:**
- Produces: `internal interface IBankToggleCounter { bool Banked { get; set; } bool HasBankToggle { get; } }`. Task 5 makes `KillCounter` implement it; Tasks 7–8 use it to find toggle-carrying counters.

- [ ] **Step 1: Write the failing test**

Append to `test/SMWCounters.Tests/BankToggleTests.cs`:

```csharp
[Fact]
public void BankedCounters_ExposeTheToggleInterface()
{
    Assert.IsAssignableFrom<IBankToggleCounter>(new PowerupCounter());
    Assert.IsAssignableFrom<IBankToggleCounter>(new CoinCounter());
    Assert.IsAssignableFrom<IBankToggleCounter>(new JumpCounter());
    Assert.False(((IBankToggleCounter)new ExitCounter()).HasBankToggle);
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter "FullyQualifiedName~BankToggleTests"`
Expected: build FAILS — `IBankToggleCounter` does not exist.

- [ ] **Step 3: Create the interface and wire it**

Create `src/SMWCounters/Counters/IBankToggleCounter.cs`:

```csharp
namespace LiveSplit.SmwCounters.Counters;

// Counters carrying the "Discard on death" toggle. Banked is a display
// selector (see BankedCounter): both histories are always tracked; the flag
// only picks which one Value/ValueIsAlert render. HasBankToggle=false means
// the settings UI shows no checkbox and Banked stays at its default (true).
internal interface IBankToggleCounter
{
    bool Banked { get; set; }
    bool HasBankToggle { get; }
}
```

In `BankedCounter.cs` change the declaration:

```csharp
internal abstract class BankedCounter : ISmwCounter, IBankToggleCounter
```

In `SmwCountersComponent.cs` `Poll()` (line ~257), replace

```csharp
if (c is BankedCounter { HasBankToggle: true } bc) { bc.Banked = Settings.IsBankOnSave(c.Id); }
```

with

```csharp
if (c is IBankToggleCounter { HasBankToggle: true } bc) { bc.Banked = Settings.IsBankOnSave(c.Id); }
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/IBankToggleCounter.cs src/SMWCounters/Counters/BankedCounter.cs src/SMWCounters/UI/Components/SmwCountersComponent.cs test/SMWCounters.Tests/BankToggleTests.cs
git commit -m "refactor: IBankToggleCounter interface so non-BankedCounter types can carry the toggle"
```

---

### Task 4: `MoonCounter` becomes a `BankedCounter`; per-level dedupe deleted

**Files:**
- Modify: `src/SMWCounters/Counters/MoonCounter.cs` (full rewrite, below)
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs` (delete the `MoonCounter` extras branch in `BuildExtras`, ~lines 133–147; inline the `var moon` local at ~line 77 back into the array initializer)
- Modify: `test/SMWCounters.Tests/MoonCounterTests.cs`
- Modify: `test/SMWCounters.Tests/StateHashTests.cs` (delete `MoonDedupeModeFlip_ChangesStateHash`)
- Modify: `test/SMWCounters.Tests/CounterPersistenceTests.cs` (replace `MoonDedupeMode_SurvivesRoundTrip`)

**Interfaces:**
- Consumes: `BankedCounter` (Task 2 semantics), `MidwayExitBankDetector` (existing).
- Produces: `MoonCounter : BankedCounter`, `SaveName => "Moons"`. `MoonDedupeMode` and `DedupeMode` cease to exist — anything referencing them must be deleted in this task.

- [ ] **Step 1: Rewrite the tests**

Replace `test/SMWCounters.Tests/MoonCounterTests.cs` entirely:

```csharp
using System.Xml;

using LiveSplit.SmwCounters.Counters;
using Xunit;

namespace SMWCounters.Tests;

public class MoonCounterTests
{
    private const int GameMode = 0x0100;
    private const int Moon = 0x13C5;
    private const int Anim = 0x0071;
    private const int Midway = 0x13CE;

    private const byte LevelMain = 0x14;

    private static void Poll(MoonCounter c, FakeSnesMemory m, byte gameMode, byte moon,
                             byte anim = 0, byte midway = 0)
    {
        m.SetByte(GameMode, gameMode);
        m.SetByte(Moon, moon);
        m.SetByte(Anim, anim);
        m.SetByte(Midway, midway);
        c.Poll(m);
    }

    [Fact]
    public void NotInLevel_MoonJump_DoesNotCount()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        // Overworld / load: game mode != level-main, moon byte holds transient data that jumps.
        Poll(c, m, 0, 0);
        Poll(c, m, 0, 5);
        Assert.Equal(0, c.Value);
    }

    [Fact]
    public void InLevel_MoonCollected_CountsOnce()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);  // entered level, 0 moons
        Poll(c, m, LevelMain, 1);  // collected a 3-up moon
        Poll(c, m, LevelMain, 1);  // held
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void CustomYoshiHouse_LegacyInLevelFlagUnset_StillCounts()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        // Regression (2026-07-27): $1935 stays 0 in custom Yoshi Houses, but
        // game mode is level-main — moons there must still count.
        m.SetByte(0x1935, 0);
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void EnteringLevel_RebaselinesFromLoadValue()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, 0, 3);          // not in level, stale value 3 — ignored
        Poll(c, m, LevelMain, 3);  // enter level; first in-level sample only baselines
        Assert.Equal(0, c.Value);
        Poll(c, m, LevelMain, 4);  // now a real collection
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void LeavingLevel_ClearsBaseline_NoCrossLevelSpuriousCount()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);  // in level; baseline
        Poll(c, m, LevelMain, 2);  // real collection
        Assert.Equal(1, c.Value);
        Poll(c, m, 0, 2);          // left level; gate clears the stale baseline
        Poll(c, m, LevelMain, 5);  // first in-level sample after re-entry only re-baselines
        Assert.Equal(1, c.Value);
        Poll(c, m, LevelMain, 6);  // now a real collection in the new level
        Assert.Equal(2, c.Value);
    }

    [Fact]
    public void Banked_MoonIsGoldUntilMidway_ThenLocked()
    {
        var c = new MoonCounter();              // Banked defaults true at counter level
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);               // collect: unbanked
        Assert.Equal(1, c.Value);
        Assert.True(c.ValueIsAlert);
        Poll(c, m, LevelMain, 1, midway: 1);    // midway 0->1 banks
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);
        Poll(c, m, LevelMain, 1, anim: 9);      // die after the bank: kept
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void Banked_DeathBeforeBank_DiscardsTheMoon()
    {
        var c = new MoonCounter();
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);               // collect: unbanked
        Poll(c, m, LevelMain, 1, anim: 9);      // die before banking
        Assert.Equal(0, c.Value);
        c.Banked = false;                        // plain history still has it
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void ToggleOff_MoonsBehaveLikeTheOldPlainTally()
    {
        var c = new MoonCounter { Banked = false };  // the settings default for moons
        var m = new FakeSnesMemory();
        Poll(c, m, LevelMain, 0);
        Poll(c, m, LevelMain, 1);
        Assert.Equal(1, c.Value);
        Assert.False(c.ValueIsAlert);            // plain view never alerts
        Poll(c, m, LevelMain, 1, anim: 9);       // die: plain view unaffected
        Assert.Equal(1, c.Value);
    }

    [Fact]
    public void LegacyLayoutWithDedupeElements_LoadsValueAndIgnoresThem()
    {
        var doc = new XmlDocument();
        XmlElement parent = doc.CreateElement("state");
        XmlElement moons = doc.CreateElement("Moons");
        moons.InnerText = "4";
        parent.AppendChild(moons);
        XmlElement mode = doc.CreateElement("DedupeMode");  // pre-v0.6 element
        mode.InnerText = "PerLevel";
        parent.AppendChild(mode);

        var c = new MoonCounter();
        c.LoadState(parent);
        Assert.Equal(4, c.Value);                // banked view (saved defaulted to 4)
        Assert.False(c.ValueIsAlert);
        c.Banked = false;
        Assert.Equal(4, c.Value);                // plain defaulted to total
    }
}
```

In `test/SMWCounters.Tests/StateHashTests.cs`: DELETE the `MoonDedupeModeFlip_ChangesStateHash` test (the property is gone).

In `test/SMWCounters.Tests/CounterPersistenceTests.cs`: REPLACE `MoonDedupeMode_SurvivesRoundTrip` with:

```csharp
[Fact]
public void MoonValue_SurvivesRoundTrip()
{
    var c = new MoonCounter();
    c.SetValue(2);

    var restored = new MoonCounter();
    RoundTrip(c, restored);

    Assert.Equal(2, restored.Value);
    Assert.Equal(((ISmwCounter)c).StateHash, ((ISmwCounter)restored).StateHash);
}
```

- [ ] **Step 2: Run to verify the state of the world**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter "FullyQualifiedName~MoonCounterTests"`
Expected: build FAILS — tests construct `MoonCounter` without `DedupeMode` and use `Banked`/`ValueIsAlert` members it doesn't have yet.

- [ ] **Step 3: Rewrite `MoonCounter`**

Replace `src/SMWCounters/Counters/MoonCounter.cs` entirely:

```csharp
using System.Drawing;

using LiveSplit.SmwCounters.Snes;

namespace LiveSplit.SmwCounters.Counters;

// Counts 3-up moon collections. A BankedCounter since v0.6.x — but the
// "Discard on death" toggle defaults OFF for moons (settings-level default),
// so out of the box this displays the plain never-reverted history, identical
// to the old standalone counter. Toggled on, moons show gold until a
// midway/exit banks them and a death discards unbanked ones.
//
// The per-level dedupe mode was dropped 2026-08-04: it served one
// hypothetical challenge run and interacted badly with death-reverts (the
// dedupe would remember a discarded moon and refuse to recount it). Legacy
// DedupeMode/DedupePerRoom layout elements load fine — they are simply never
// read.
internal sealed class MoonCounter : BankedCounter
{
    // SNES WRAM addresses (from kaizosplits Memory.cs).
    private const int MoonCounterOffset = 0x13C5; // # of 3-up moons collected, per scene
    private const int GameModeOffset    = 0x0100;

    private const byte LevelMainMode    = 0x14;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.moon.png");

    private readonly PreviousByte previousMoon = new();
    private readonly MidwayExitBankDetector bank = new();

    public override string Id => "moons";
    public override Image DefaultIcon => icon;
    public override string DefaultLabel => "Moons";
    protected override string SaveName => "Moons";

    protected override int DetectCollectDelta(ISnesMemory memory)
    {
        // Only count while actually in a level. Outside a level (title,
        // file-select, overworld, load transitions) $13C5 holds transient data
        // whose changes fire spuriously — clear the baseline so re-entry
        // establishes a fresh one instead of registering an edge.
        // Gate on game mode (level-main), not the legacy $1935 in-level flag:
        // custom Yoshi Houses never set $1935, so moons there wouldn't count
        // (live-confirmed 2026-07-27). Mirrors JumpCounter's gate.
        if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
        {
            previousMoon.Clear();
            return 0;
        }
        if (!memory.ReadWramByte(MoonCounterOffset, out byte moon))
        {
            previousMoon.Clear();
            return 0;
        }
        bool collected = previousMoon.HasPrevious && moon > previousMoon.Value;
        previousMoon.Set(moon);
        return collected ? 1 : 0;
    }

    protected override bool DetectBank(ISnesMemory memory) => bank.DetectBank(memory);

    protected override void ClearDetectors()
    {
        previousMoon.Clear();
        bank.Clear();
    }
}
```

- [ ] **Step 4: Clean up the component**

In `src/SMWCounters/UI/Components/SmwCountersComponent.cs`:

1. In the constructor, delete `var moon = new MoonCounter();` and put `new MoonCounter(),` directly in the `counters` array initializer where `moon,` was.
2. In `BuildExtras`, delete the whole `if (counter is MoonCounter moon) { ... }` branch (the "One per level" checkbox). Moons now returns `(null, null)` via the fallthrough.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS. If anything else still references `MoonDedupeMode`, the build names it — delete that reference (it is dead by design).

- [ ] **Step 6: Commit**

```bash
git add src/SMWCounters/Counters/MoonCounter.cs src/SMWCounters/UI/Components/SmwCountersComponent.cs test/SMWCounters.Tests/MoonCounterTests.cs test/SMWCounters.Tests/StateHashTests.cs test/SMWCounters.Tests/CounterPersistenceTests.cs
git commit -m "feat: Moons is a banked counter; per-level dedupe removed"
```

---

### Task 5: `KillCounter` banking mechanics (poll restructure)

**Files:**
- Modify: `src/SMWCounters/Counters/KillCounter.cs`
- Modify: `test/SMWCounters.Tests/KillCounterTests.cs`

**Interfaces:**
- Consumes: `DeathEdgeDetector` (Task 1), `MidwayExitBankDetector` (existing), `IBankToggleCounter` (Task 3).
- Produces: `KillCounter : ISmwCounter, IBankToggleCounter` with fields `kills, killsSaved, killsPlain, destruction, destructionSaved, destructionPlain`, `Banked` (default true), `HasBankToggle => true`. Task 6 builds display/persistence on these exact names.

**Why the restructure:** today the entire `Poll` gates on game mode `0x14`, but bank signals fire after the mode leaves level-main (2026-08-04 log: goal exit `BNK exitMode 00->01` at `mode=0C`; `$1F2E` backstop on the overworld at `mode=0E`). Death exits park `$0DD5` at `0x80`, which `LevelExitDetector` excludes — dying must never bank.

- [ ] **Step 1: Write the failing tests**

Append to `test/SMWCounters.Tests/KillCounterTests.cs` (add `private const int Anim = 0x0071, Midway = 0x13CE, ExitMode = 0x0DD5;` next to the existing offset consts):

```csharp
[Fact]
public void DeathEdge_RevertsBothTalliesToSaved()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0);
    PollSlot0(c, m, Alive);
    PollSlot0(c, m, Spinjump);            // kill+destruction 1, unbanked
    m.SetByte(Anim, 9);
    PollSlot0(c, m, Spinjump);            // the death edge
    Assert.Equal(0, Kills(c));
    Assert.Equal(0, Destruction(c));
}

[Fact]
public void MidwayBank_LocksBothTallies_DeathAfterKeepsThem()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0); m.SetByte(Midway, 0);
    PollSlot0(c, m, Alive);
    PollSlot0(c, m, Spinjump);            // 1/1 unbanked
    m.SetByte(Midway, 1);
    PollSlot0(c, m, Spinjump);            // midway 0->1 banks
    m.SetByte(Anim, 9);
    PollSlot0(c, m, Spinjump);            // die after the bank
    Assert.Equal(1, Kills(c));
    Assert.Equal(1, Destruction(c));
}

// The restructure's reason for existing: the exit flag lands at mode 0C
// (2026-08-04 log, 14:25:44) — the sprite scan is gated off there, but the
// bank must still fire.
[Fact]
public void ExitBank_FiresOutsideLevelMainMode()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0); m.SetByte(ExitMode, 0);
    PollSlot0(c, m, Alive);
    PollSlot0(c, m, Spinjump);                    // 1/1 unbanked
    m.SetByte(ExitMode, 1);                       // goal exit flag
    PollSlot0(c, m, Spinjump, gameMode: 0x0C);    // level-end fade: banks
    m.SetByte(ExitMode, 0);
    PollSlot0(c, m, Alive, gameMode: Level);      // next level
    m.SetByte(Anim, 9);
    PollSlot0(c, m, Alive);                       // die there
    Assert.Equal(1, Kills(c));                    // the exit locked them in
    Assert.Equal(1, Destruction(c));
}

// Death exits park $0DD5 at 0x80 — excluded by LevelExitDetector. Dying
// must not bank what the same death is discarding.
[Fact]
public void DeathExitParking_DoesNotBank()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0); m.SetByte(ExitMode, 0);
    PollSlot0(c, m, Alive);
    PollSlot0(c, m, Spinjump);                    // 1/1 unbanked
    m.SetByte(Anim, 9);
    PollSlot0(c, m, Spinjump);                    // die: revert to 0/0
    m.SetByte(ExitMode, 0x80);
    PollSlot0(c, m, Spinjump, gameMode: 0x0B);    // death-exit parking: no bank event
    Assert.Equal(0, Kills(c));
    Assert.Equal(0, Destruction(c));
}

// Death invalidates in-flight evidence: a pending fireball-coin kill must
// not resolve after the death that reverted its destruction credit.
[Fact]
public void Death_CancelsPendingFireballCoinEvidence()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0);
    PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
    PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // E6: pending, destruction 1
    m.SetByte(Anim, 9);
    PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);  // die: revert + evidence cleared
    m.SetByte(Anim, 0);
    PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);   // despawn + coin after death
    Assert.Equal(0, Kills(c));                            // no posthumous kill
    Assert.Equal(0, Destruction(c));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter "FullyQualifiedName~KillCounterTests"`
Expected: the five new tests FAIL (no death/bank detection exists); all pre-existing tests still pass.

- [ ] **Step 3: Implement the mechanics**

In `src/SMWCounters/Counters/KillCounter.cs`:

1. Declaration: `internal sealed class KillCounter : ISmwCounter, IBankToggleCounter`.
2. Add fields next to `kills`/`destruction`:

```csharp
private int kills;
private int destruction;
private int killsSaved;
private int destructionSaved;
private int killsPlain;
private int destructionPlain;

private readonly DeathEdgeDetector deathEdge = new();
private readonly MidwayExitBankDetector bank = new();
```

3. Add the interface members (display semantics land in Task 6 — for now only these):

```csharp
// Display selector, synced per poll from the "Discard on death" setting.
public bool Banked { get; set; } = true;
public bool HasBankToggle => true;
```

4. Add increment helpers and replace EVERY `kills++` with `AddKill();` and every `destruction++` with `AddDestruction();` (sites: the coin-window resolve in `Poll`, and in `PollSlot` the E6, E4-swallow, E2, E1 [both lines], and E8 branches):

```csharp
private void AddKill() { kills++; killsPlain++; }
private void AddDestruction() { destruction++; destructionPlain++; }
```

5. Restructure `Poll` — the current body from the game-mode gate down moves into a new `ScanSprites` method, unchanged except for the increment-helper renames:

```csharp
public void Poll(ISnesMemory memory)
{
    if (!memory.IsAttached)
    {
        deathEdge.Clear();
        bank.Clear();
        ClearAll();
        return;
    }

    // Death discard and banking watch the whole game, not just level-main:
    // the exit-flag edge lands after the mode leaves $14 (2026-08-04 log:
    // BNK exitMode 00->01 at mode=0C), and $0071 death edges fire fine in
    // mode 14. Death exits park $0DD5 at 0x80, which LevelExitDetector
    // excludes — dying never banks.
    if (deathEdge.Detect(memory))
    {
        kills = killsSaved;
        destruction = destructionSaved;
        ClearInFlightEvidence();
    }

    ScanSprites(memory);

    if (bank.DetectBank(memory))
    {
        killsSaved = kills;
        destructionSaved = destruction;
    }
}

// Death invalidates in-flight evidence: pending fireball coins, open coin
// windows, recorded mouth entries and tongue lingers all belong to the
// attempt that just ended.
private void ClearInFlightEvidence()
{
    for (int i = 0; i < SlotCount; i++)
    {
        mouthEntry[i] = MouthEntry.None;
        pendingCoin[i] = false;
        coinWindow[i] = 0;
        tongueLinger[i] = 0;
    }
}

private void ScanSprites(ISnesMemory memory)
{
    if (!memory.ReadWramByte(GameModeOffset, out byte gameMode) || gameMode != LevelMainMode)
    {
        ClearAll();
        return;
    }
    // ... existing body: coinsChanged, slot buffers, tongue marks, PollSlot
    // loop, coin-window resolve, window aging — verbatim from the old Poll.
}
```

6. `Reset()`: also zero `killsSaved`, `destructionSaved`, `killsPlain`, `destructionPlain` and call `deathEdge.Clear(); bank.Clear();`.
7. `LoadState`: add `deathEdge.Clear(); bank.Clear();` next to the existing `ClearAll();` (persistence values land in Task 6).

- [ ] **Step 4: Run the full suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS — including every pre-existing KillCounter test (they never set `$0071`/`$13CE`/`$0DD5`, so the new detectors stay inert for them).

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/KillCounterTests.cs
git commit -m "feat: KillCounter banks at checkpoints/exits and discards on death; bank/death detection runs at any game mode"
```

---

### Task 6: `KillCounter` display, `SetValue`, `StateHash`, serialization

**Files:**
- Modify: `src/SMWCounters/Counters/KillCounter.cs`
- Modify: `test/SMWCounters.Tests/KillCounterTests.cs`

**Interfaces:**
- Consumes: the six fields + `Banked` from Task 5.
- Produces: final `KillCounter` public behavior — `Value`/`ValueIsAlert` honoring `Mode` + `Banked`; serialization elements `KillsSaved`/`KillsPlain`/`DestructionSaved`/`DestructionPlain` (missing → defaults to the tally).

- [ ] **Step 1: Write the failing tests**

Append to `test/SMWCounters.Tests/KillCounterTests.cs`:

```csharp
[Fact]
public void Alert_TracksTheDisplayedTallysUnbankedState()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    PollSlot0(c, m, Carryable, PSwitch);
    PollSlot0(c, m, Spinjump, PSwitch);   // kills 0, destruction 1 (unbanked)
    c.Mode = KillCountMode.Kills;
    Assert.False(c.ValueIsAlert);          // kills 0 == killsSaved 0
    c.Mode = KillCountMode.Destruction;
    Assert.True(c.ValueIsAlert);           // destruction 1 != saved 0
}

[Fact]
public void BankedOff_ShowsThePlainHistory_NeverAlerts()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0);
    PollSlot0(c, m, Alive);
    PollSlot0(c, m, Spinjump);            // 1/1 unbanked
    m.SetByte(Anim, 9);
    PollSlot0(c, m, Spinjump);            // die: banked history reverts
    Assert.Equal(0, Kills(c));            // banked view
    c.Banked = false;
    c.Mode = KillCountMode.Kills;
    Assert.Equal(1, c.Value);             // plain view remembers the kill
    Assert.False(c.ValueIsAlert);
}

[Fact]
public void SetValue_SetsAllThreeHistoriesOfTheDisplayedTally()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    c.Mode = KillCountMode.Kills;
    c.SetValue(5);
    m.SetByte(Anim, 0);
    PollSlot0(c, m, Alive);
    m.SetByte(Anim, 9);
    PollSlot0(c, m, Alive);               // die: revert is a no-op (saved is 5)
    Assert.Equal(5, Kills(c));
    Assert.False(c.ValueIsAlert);
    c.Banked = false;
    c.Mode = KillCountMode.Kills;
    Assert.Equal(5, c.Value);             // plain was set too
}

[Fact]
public void SaveLoad_RoundTripsAllSixValues()
{
    var c = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0);
    PollSlot0(c, m, Alive);
    PollSlot0(c, m, Spinjump);            // 1/1, saveds 0, plains 1
    m.SetByte(Anim, 9);
    PollSlot0(c, m, Spinjump);            // die: tallies 0, plains 1

    var doc = new XmlDocument();
    var parent = doc.CreateElement("kills");
    c.SaveState(doc, parent);
    Assert.Equal("0", parent["Kills"].InnerText);
    Assert.Equal("0", parent["KillsSaved"].InnerText);
    Assert.Equal("1", parent["KillsPlain"].InnerText);
    Assert.Equal("1", parent["DestructionPlain"].InnerText);

    var restored = new KillCounter();
    restored.LoadState(parent);
    restored.Mode = KillCountMode.Kills;
    Assert.Equal(0, restored.Value);
    restored.Banked = false;
    Assert.Equal(1, restored.Value);
}

[Fact]
public void LoadLegacyDocument_TreatsTalliesAsFullyBanked()
{
    var doc = new XmlDocument();
    var parent = doc.CreateElement("kills");
    var kills = doc.CreateElement("Kills");
    kills.InnerText = "5";
    parent.AppendChild(kills);

    var c = new KillCounter();
    c.LoadState(parent);
    c.Mode = KillCountMode.Kills;
    Assert.Equal(5, c.Value);
    Assert.False(c.ValueIsAlert);          // saved defaulted to the tally
    c.Banked = false;
    Assert.Equal(5, c.Value);              // plain defaulted to the tally
}

[Fact]
public void StateHash_SeesSavedAndPlain_NotJustTheDisplayedValue()
{
    var a = new KillCounter(); var m = new FakeSnesMemory();
    m.SetByte(Anim, 0);
    PollSlot0(a, m, Alive);
    PollSlot0(a, m, Spinjump);
    m.SetByte(Anim, 9);
    PollSlot0(a, m, Spinjump);            // displayed kills 0, plain 1
    var b = new KillCounter();            // untouched: everything 0
    Assert.Equal(Kills(a), Kills(b));
    Assert.NotEqual(((ISmwCounter)a).StateHash, ((ISmwCounter)b).StateHash);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter "FullyQualifiedName~KillCounterTests"`
Expected: the new tests FAIL (`Value` ignores `Banked`, no `Plain` elements, `SetValue` doesn't touch saved/plain, `ValueIsAlert` is hardcoded false).

- [ ] **Step 3: Implement display + persistence**

In `src/SMWCounters/Counters/KillCounter.cs` replace the corresponding members:

```csharp
public int Value => Mode == KillCountMode.Kills
    ? (Banked ? kills : killsPlain)
    : (Banked ? destruction : destructionPlain);

// Gold while the displayed tally has unbanked events; the plain view never
// alerts (nothing is ever at risk there).
public bool ValueIsAlert => Banked
    && (Mode == KillCountMode.Kills ? kills != killsSaved : destruction != destructionSaved);

// Everything SaveState persists feeds the hash — a bank commit or a plain
// divergence changes what gets written without moving the displayed Value.
public int StateHash
{
    get
    {
        int h = kills;
        h = h * 397 ^ destruction;
        h = h * 397 ^ killsSaved;
        h = h * 397 ^ destructionSaved;
        h = h * 397 ^ killsPlain;
        h = h * 397 ^ destructionPlain;
        return h * 397 ^ (int)Mode;
    }
}

public void SetValue(int value)
{
    if (Mode == KillCountMode.Kills)
    {
        kills = value; killsSaved = value; killsPlain = value;
    }
    else
    {
        destruction = value; destructionSaved = value; destructionPlain = value;
    }
}

public void SaveState(XmlDocument doc, XmlElement parent)
{
    SettingsHelper.CreateSetting(doc, parent, "Kills", kills);
    SettingsHelper.CreateSetting(doc, parent, "KillsSaved", killsSaved);
    SettingsHelper.CreateSetting(doc, parent, "KillsPlain", killsPlain);
    SettingsHelper.CreateSetting(doc, parent, "Destruction", destruction);
    SettingsHelper.CreateSetting(doc, parent, "DestructionSaved", destructionSaved);
    SettingsHelper.CreateSetting(doc, parent, "DestructionPlain", destructionPlain);
    SettingsHelper.CreateSetting(doc, parent, "KillMode", Mode.ToString());
}
```

And in `LoadState`, after the existing `kills`/`destruction` parses (keep the `KillMode` parsing and the `ClearAll()`/detector clears):

```csharp
kills = SettingsHelper.ParseInt(parent["Kills"], 0);
destruction = SettingsHelper.ParseInt(parent["Destruction"], 0);
// Back-compat: pre-v0.6 layouts have no Saved/Plain -> treat as fully banked.
killsSaved = SettingsHelper.ParseInt(parent["KillsSaved"], kills);
killsPlain = SettingsHelper.ParseInt(parent["KillsPlain"], kills);
destructionSaved = SettingsHelper.ParseInt(parent["DestructionSaved"], destruction);
destructionPlain = SettingsHelper.ParseInt(parent["DestructionPlain"], destruction);
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS. Pre-existing tests that matter here: `DualTallies_ModeSelectsDisplay_SetValueWritesSelectedOnly` (still passes — the other tally's triple is untouched), `SaveLoad_RoundTripsBothTalliesAndMode`, `LoadV1Document_DefaultsDestructionZeroModeKills`, and `StateHashTests.KillModeFlip_ChangesStateHash`.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/KillCounterTests.cs
git commit -m "feat: KillCounter display selector, six-value persistence, alert on the displayed tally"
```

---

### Task 7: Settings storage — explicit `BankOnSave` map with per-counter defaults

**Files:**
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs`
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs` (BuildUi tuple gains `bool HasBankToggle`)
- Modify: `test/SMWCounters.Tests/SettingsHashTests.cs`

**Interfaces:**
- Consumes: `IBankToggleCounter` (Task 3).
- Produces: `IsBankOnSave(string)` semantics with defaults (`moons` → false, others → true); `internal static bool DefaultBankOnSave(string counterId)`; `internal static int CombineSetHashes(int hash, IEnumerable<string> enabledIds, IEnumerable<KeyValuePair<string, bool>> bankOnSave)`; `BuildUi` tuple item `bool HasBankToggle` (Task 8 renders the checkbox from it); private `List<string> toggleIds` filled by `BuildUi`.

- [ ] **Step 1: Rewrite the hash tests**

Replace the body of `test/SMWCounters.Tests/SettingsHashTests.cs` (keep the file-level comment, adjust it to mention the map):

```csharp
using System.Collections.Generic;

using LiveSplit.UI.Components;
using Xunit;

namespace SMWCounters.Tests;

// The settings hash folds the enabled set and the BankOnSave map with
// distinct salts so an id appearing in both cannot cancel out, and so
// flipping a toggle VALUE (not just membership) dirties the layout.
// Static so no WinForms control (and no CompositeHook) is constructed.
public class SettingsHashTests
{
    private static readonly string[] None = System.Array.Empty<string>();
    private static readonly KeyValuePair<string, bool>[] NoBank =
        System.Array.Empty<KeyValuePair<string, bool>>();

    private static KeyValuePair<string, bool> On(string id) => new(id, true);
    private static KeyValuePair<string, bool> Off(string id) => new(id, false);

    [Fact]
    public void IdInBothStructures_DoesNotCancelToEmpty()
    {
        int both = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, new[] { Off("coins") });
        int neither = SmwCountersComponentSettings.CombineSetHashes(0, None, NoBank);
        Assert.NotEqual(neither, both);
    }

    [Fact]
    public void SameId_DifferentStructure_HashesDifferently()
    {
        int inEnabled = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, NoBank);
        int inBank = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { Off("coins") });
        Assert.NotEqual(inEnabled, inBank);
    }

    [Fact]
    public void FlippingAToggleValue_ChangesTheHash()
    {
        int on = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { On("coins") });
        int off = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { Off("coins") });
        Assert.NotEqual(on, off);
    }

    [Fact]
    public void IterationOrder_DoesNotMatter()
    {
        int ab = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "a", "b" }, new[] { On("x"), Off("y") });
        int ba = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "b", "a" }, new[] { Off("y"), On("x") });
        Assert.Equal(ab, ba);
    }

    [Fact]
    public void DefaultBankOnSave_MoonsOff_EverythingElseOn()
    {
        Assert.False(SmwCountersComponentSettings.DefaultBankOnSave("moons"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("kills"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("jumps"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("coins"));
        Assert.True(SmwCountersComponentSettings.DefaultBankOnSave("powerups"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter "FullyQualifiedName~SettingsHashTests"`
Expected: build FAILS — `CombineSetHashes` has the old signature and `DefaultBankOnSave` does not exist.

- [ ] **Step 3: Implement the map**

In `src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs`:

1. Replace the `bankDisabled` field:

```csharp
// Per-counter "Discard on death" values. Ids absent from the map use
// DefaultBankOnSave — an explicit map (unlike the pre-v0.6 BankDisabled
// set) can tell "user turned it on" apart from "never seen", which is what
// lets moons default OFF while everything else defaults ON.
private readonly Dictionary<string, bool> bankOnSave = new();

// Toggle-carrying counter ids, captured by BuildUi. Serialization writes
// one explicit entry per id so future default changes can't reinterpret
// existing layouts.
private readonly List<string> toggleIds = new();
```

2. Replace the accessors:

```csharp
public bool IsBankOnSave(string counterId)
    => bankOnSave.TryGetValue(counterId, out bool value) ? value : DefaultBankOnSave(counterId);

public void SetBankOnSave(string counterId, bool value) => bankOnSave[counterId] = value;

// Moons banking is opt-in (a moon lost to a death revert feels punitive by
// default); every other toggle counter keeps the established default ON.
internal static bool DefaultBankOnSave(string counterId) => counterId != "moons";
```

3. `BuildUi`: change the signature's tuple to `(string Id, string DefaultLabel, bool HasBankToggle, Control Extras, Action ResetValue, Func<int> GetValue, Action<int> SetValue, Action RefreshExtras)` (and the deconstruction in the `foreach`). At the top of `BuildUi` add `toggleIds.Clear();`, and inside the row loop add:

```csharp
if (hasBankToggle) { toggleIds.Add(id); }
```

4. `SetSettings`: replace the `bankDisabled` block:

```csharp
bankOnSave.Clear();
XmlElement bankMapNode = e["BankOnSave"];
if (bankMapNode != null)
{
    foreach (XmlElement c in bankMapNode.GetElementsByTagName("Counter"))
    {
        string id = c.GetAttribute("id");
        if (!string.IsNullOrEmpty(id) && bool.TryParse(c.InnerText, out bool on))
        {
            bankOnSave[id] = on;
        }
    }
}
// Legacy pre-v0.6 layouts: BankDisabled membership meant "toggle off"; ids
// absent from both structures fall through to DefaultBankOnSave.
XmlElement bankNode = e["BankDisabled"];
if (bankNode != null)
{
    foreach (XmlElement c in bankNode.GetElementsByTagName("Counter"))
    {
        if (!string.IsNullOrEmpty(c.InnerText) && !bankOnSave.ContainsKey(c.InnerText))
        {
            bankOnSave[c.InnerText] = false;
        }
    }
}
```

5. `CreateSettingsNode`: build the explicit pair list once, write it (replacing the `BankDisabled` write — it is no longer written), and hash it:

```csharp
var bankPairs = new List<KeyValuePair<string, bool>>();
foreach (string id in toggleIds)
{
    bankPairs.Add(new KeyValuePair<string, bool>(id, IsBankOnSave(id)));
}

if (document != null && parent != null)
{
    // ... existing EnabledCounters write stays ...

    XmlElement bankMapNode = document.CreateElement("BankOnSave");
    foreach (KeyValuePair<string, bool> kv in bankPairs)
    {
        XmlElement c = document.CreateElement("Counter");
        c.SetAttribute("id", kv.Key);
        c.InnerText = kv.Value.ToString();
        bankMapNode.AppendChild(c);
    }
    parent.AppendChild(bankMapNode);
}

return CombineSetHashes(hash, enabled, bankPairs);
```

6. Replace `CombineSetHashes`:

```csharp
// Fold the enabled set and the BankOnSave pairs into the settings hash.
// Each structure folds commutatively (iteration order is unspecified) into
// its own sub-hash with a value-dependent salt for the pairs, then the
// sub-hashes combine order-sensitively so the structures can't cancel.
internal static int CombineSetHashes(int hash, IEnumerable<string> enabledIds,
    IEnumerable<KeyValuePair<string, bool>> bankOnSave)
{
    int enabledHash = 0;
    int bankHash = 0;
    foreach (string id in enabledIds) { enabledHash ^= id.GetHashCode(); }
    foreach (KeyValuePair<string, bool> kv in bankOnSave)
    {
        bankHash ^= kv.Key.GetHashCode() * (kv.Value ? 397 : 31);
    }
    hash = hash * 397 ^ enabledHash;
    return hash * 397 ^ bankHash;
}
```

(`using System.Collections.Generic;` may need adding to the file's usings.)

7. In `SmwCountersComponent.cs` constructor, update the `rows` tuple list type and the `rows.Add` call to pass the new item:

```csharp
var rows = new List<(string Id, string DefaultLabel, bool HasBankToggle, Control Extras, Action ResetValue, Func<int> GetValue, Action<int> SetValue, Action RefreshExtras)>();
foreach (ISmwCounter c in counters)
{
    ISmwCounter counter = c; // capture per-iteration
    (Control extras, Action refreshExtras) = BuildExtras(counter);
    bool hasBankToggle = counter is IBankToggleCounter { HasBankToggle: true };
    rows.Add((counter.Id, counter.DefaultLabel, hasBankToggle, extras, () => counter.Reset(),
              () => counter.Value, v => counter.SetValue(v), refreshExtras));
}
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs src/SMWCounters/UI/Components/SmwCountersComponent.cs test/SMWCounters.Tests/SettingsHashTests.cs
git commit -m "feat: explicit BankOnSave map with per-counter defaults (moons OFF); legacy BankDisabled still reads"
```

---

### Task 8: Settings UI — "Discard on death" column

**Files:**
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs` (`CounterRow`, `BuildUi`, `SyncRowEnabled`, `RefreshFromModel`)
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs` (`BuildExtras`: delete the `BankedCounter` labeled-checkbox branch)

**Interfaces:**
- Consumes: `HasBankToggle` tuple item + `toggleIds` (Task 7), `IsBankOnSave`/`SetBankOnSave`.
- Produces: user-visible column; no new code contracts. This is WinForms layout code — there are no automated UI tests in this repo; verification is build + full suite + a manual pass.

- [ ] **Step 1: Delete the old per-row labeled checkbox**

In `SmwCountersComponent.BuildExtras`, delete the entire `if (counter is BankedCounter { HasBankToggle: true }) { ... }` branch (the "Discard on death" `CheckBox` panel). After this, `BuildExtras` returns extras only for `KillCounter` (the radios); everything else falls through to `(null, null)`.

- [ ] **Step 2: Add the column**

In `SmwCountersComponentSettings.cs`:

1. `CounterRow` gains: `public CheckBox BankToggle;`
2. Add a field next to the other controls: `private readonly ToolTip bankToolTip = new();`
3. In `BuildUi`, before the row loop (after `int y = 10;`), add the header and shift the rows down:

```csharp
var lblBank = new Label
{
    Text = "Discard on death",
    Location = new Point(232, y),
    AutoSize = true,
};
bankToolTip.SetToolTip(lblBank,
    "Unbanked collects (shown gold) are discarded if you die before a checkpoint or exit. " +
    "Both histories are always tracked — the checkbox only picks which one is shown.");
Controls.Add(lblBank);
y += 20;
```

4. Inside the row loop, after the `ResetValue` button is added and before the extras block:

```csharp
if (hasBankToggle)
{
    row.BankToggle = new CheckBox
    {
        Location = new Point(252, y + 2),
        AutoSize = true,
        Checked = IsBankOnSave(id),
    };
    row.BankToggle.CheckedChanged += (_, __) => SetBankOnSave(id, row.BankToggle.Checked);
    bankToolTip.SetToolTip(row.BankToggle, "Discard on death for " + defaultLabel + ".");
    Controls.Add(row.BankToggle);
}
```

(The `toggleIds.Add(id)` from Task 7 lives in this same `if` — merge them into one block.)

5. `SyncRowEnabled`: add `if (row.BankToggle != null) { row.BankToggle.Enabled = on; }`
6. `RefreshFromModel`, inside the row loop: add `if (row.BankToggle != null) { row.BankToggle.Checked = IsBankOnSave(row.Id); }`

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build src/SMWCounters/SMWCounters.csproj && dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: build clean, ALL tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs src/SMWCounters/UI/Components/SmwCountersComponent.cs
git commit -m "feat: settings dialog gets a Discard-on-death checkbox column; per-row labeled checkboxes removed"
```

---

### Task 9: Final verification and deploy build

**Files:** none new — verification only.

- [ ] **Step 1: Full suite, one more time**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: ALL PASS.

- [ ] **Step 2: Release build (deploy) — LiveSplit must be CLOSED**

Ask Andrew to close LiveSplit if it is running, then:

Run: `dotnet build -c Release src/SMWCounters/SMWCounters.csproj`
Expected: "Build succeeded" with ZERO MSB3026/MSB3027 warnings. If those warnings appear, the Components copy failed (LiveSplit still holds the DLL lock) — close LiveSplit and rebuild. Then confirm the deploy landed:

Run (Git Bash): `ls -la --time-style=full-iso /c/Apps/LiveSplit/Components/SMWCounters.dll`
Expected: mtime within the last minute.

- [ ] **Step 3: Manual smoke checklist (Andrew, in LiveSplit)**

- Settings dialog: "Discard on death" header + checkboxes on Jumps, Powerups, Coins, Kills, Moons; none on Deaths/Exits; Kills row keeps the Kills/Destruction radios; Moons row has no extras.
- Defaults on a fresh layout: Kills checked, Moons unchecked.
- Live: kill something → Kills turns gold; hit a checkpoint → white; die before a checkpoint → reverts. Collect a moon with its toggle ON → gold until checkpoint.
- Mid-run toggle flip on Kills swaps between the two histories.

- [ ] **Step 4: Commit anything outstanding**

If the smoke test demanded no changes, working tree should already be clean (`git status`). Otherwise: fix, re-run the suite, commit.
