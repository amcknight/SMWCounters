# 1-Ups Counter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A banked "1-Ups" counter that counts collected 1-Up mushroom sprites (`$78`, `$7F`) and Fishin' Lakitu's bait, confirmed by the game's own 1UP score sprite within 3 polls, plus +1/−1 global hotkeys that adjust one chosen counter.

**Architecture:** Two pieces the Kill counter already carries privately — per-slot sprite-table tracking and a "despawn, then confirm within N polls" window — become small shared classes (`SpriteSlotTracker`, `ConfirmationWindow`) that `KillCounter` adopts unchanged in behavior and the new `OneUpCounter : BankedCounter` composes. The confirmation signal is a `$16E1` score-sprite slot taking a value in `$0D`–`$10` it did not hold last poll. Hotkeys reuse the Reset hotkey's `CompositeHook` plumbing; adjustments go through `SetValue` so they bank immediately, and re-capture the ended-phase display freeze. The debug log gains `SCR`/`LAK` traces and a trace sink the counters write `1UP`/`COIN` lines through.

**Tech Stack:** C# / .NET Framework 4.8.1, WinForms (LiveSplit component), xunit.

**Spec:** `docs/superpowers/specs/2026-10-24-oneup-counter-design.md`

## Global Constraints

- Test command (all tasks): `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj -c Release --nologo`. Filtered: append `--filter "FullyQualifiedName~<TestClassName>"`. Baseline before Task 1: `Passed: 218`.
- The FULL suite must be green before every commit; `KillCounterTests` must pass **unchanged** through Tasks 3–7 (they pin the shared pieces' semantics).
- Committing directly to `master` is authorized for this repo (CLAUDE.md). Commit messages: `feat:`/`refactor:`/`docs:` prefix, end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Production types are `internal`; the test project sees them via `InternalsVisibleTo`. Never widen to `public` for testability.
- Serialization names are a stable on-disk API: counter id `oneups`, elements `OneUps`/`OneUpsSaved`/`OneUpsPlain` (from `BankedCounter.SaveName = "OneUps"`), settings elements `IncrementKey`, `DecrementKey`, `HotkeyTarget`.
- Spec values: 1-Up sprites `0x78` and `0x7F`; Lakitu `0x1E` with fishing flag at `$151C + slot`; score sprite table `$16E1`, 6 slots, 1UP family `0x0D..0x10`; candidate status `>= 0x07`; `OneUpCounter.ConfirmWindowPolls = 3`; Kill counter's `CoinKillWindowPolls = 4` stays; `DefaultBankOnSave("oneups") == false`; `HotkeyTarget` default `"oneups"`.
- Within one poll the counter reads the score sprite table **before** the sprite slots, so a frame boundary between the two reads can only delay the confirmation, never put it ahead of the candidate.
- `dotnet build -c Release` copies the DLL to the local LiveSplit Components folder (`SMWCounters.local.props`) and warns MSB3026/MSB3027 if LiveSplit is open; a warning means a stale DLL would be live-tested. Close LiveSplit before Release builds.
- Windows shell: plain `cd`, never `cd /d`. Trace lines must never throw: a null `Trace` sink is silent.

## Review Focus

1. **Read order inside a poll.** A score sprite seen one poll *before* its vanish would confirm nothing and the vanish would then expire unconfirmed. The spec fixes the order (score table first); the fake memory is atomic so no unit test can see a straddle. Pinned indirectly by `OneUpCounterTests.Collect_ScoreSpriteSamePoll_Counts` (arm must precede confirm within one poll). Reviewer: check `DetectCollectDelta` reads `$16E1..$16E6` before `slots.Read`.
2. **An unmatched 1UP text must not be remembered.** A Dragon Coin 1UP with nothing armed, followed later by an off-screen `$78` despawn, must count nothing. Test: `OneUpCounterTests.ScoreSprite_NoVanish_ThenLaterVanish_NoCount` (Task 4).
3. **Leaving the level mid-window.** Gate closes with a window armed; re-entry shows a 1UP text (stale slot) → must not count. Test: `OneUpCounterTests.GameModeGate_DropsArmedWindow` (Task 4).
4. **A slot read that fails mid-run.** `Ok(i)` false for one poll must clear that slot's previous so its return cannot look like a vanish. Test: `OneUpCounterTests.ReadFailure_ThenReturn_NoCandidate` (Task 4).
5. **A hotkey bound to a counter id this build does not know** (layout saved by a newer version). Must be a silent no-op, never an exception on the hook thread. Test: `CounterAdjustTests.Find_UnknownId_ReturnsNull` (Task 6).

---

### Task 1: `ConfirmationWindow` shared class

**Files:**
- Create: `src/SMWCounters/Counters/ConfirmationWindow.cs`
- Test: `test/SMWCounters.Tests/ConfirmationWindowTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal sealed class ConfirmationWindow
  {
      public ConfirmationWindow(int slotCount, int windowPolls, string traceTag);
      public int WindowPolls { get; }
      public Action<string> Trace { get; set; }   // null = silent
      public void Arm(int slot);        // remaining[slot] = WindowPolls + 1; silent
      public bool IsArmed(int slot);    // remaining[slot] > 0
      public void Disarm(int slot);     // remaining[slot] = 0; silent
      public int Confirm(int count);    // confirms up to `count` armed slots, oldest first
                                        // (smallest remaining; ties → lowest slot index);
                                        // each traces "{tag} confirmed slot{n} polls={WindowPolls + 1 - remaining}";
                                        // returns how many confirmed
      public void Tick();               // remaining-- on every armed slot; a slot reaching 0
                                        // traces "{tag} expired slot{n}"
      public void Clear();              // all remaining = 0; silent
  }
  ```
  Semantics are exactly `KillCounter.coinWindow` today: arm sets `polls + 1`, the counter calls `Confirm` then `Tick` at the end of the same poll, so a confirmation `k` polls after arming reports `polls=k` and the last confirmable poll is `k == WindowPolls`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class ConfirmationWindowTests
{
    private static (ConfirmationWindow w, List<string> log) Make(int polls = 4)
    {
        var log = new List<string>();
        var w = new ConfirmationWindow(12, polls, "T") { Trace = log.Add };
        return (w, log);
    }

    [Fact] public void Arm_ConfirmSamePoll_PollsZero()
    { var (w, log) = Make(); w.Arm(0); Assert.Equal(1, w.Confirm(1)); Assert.False(w.IsArmed(0)); Assert.Contains("T confirmed slot0 polls=0", log); }

    [Fact] public void Confirm_TwoPollsLate_ReportsTwo()
    { var (w, log) = Make(); w.Arm(0); w.Tick(); w.Tick(); Assert.Equal(1, w.Confirm(1)); Assert.Contains("T confirmed slot0 polls=2", log); }

    [Fact] public void Confirm_OnLastPoll_StillCounts()
    { var (w, _) = Make(polls: 4); w.Arm(0); for (int i = 0; i < 4; i++) { w.Tick(); } Assert.True(w.IsArmed(0)); Assert.Equal(1, w.Confirm(1)); }

    [Fact] public void Expiry_TracesAndConfirmsNothing()
    { var (w, log) = Make(polls: 4); w.Arm(0); for (int i = 0; i < 5; i++) { w.Tick(); } Assert.False(w.IsArmed(0)); Assert.Equal(0, w.Confirm(1)); Assert.Contains("T expired slot0", log); Assert.Equal(1, log.Count(l => l.Contains("expired"))); }

    [Fact] public void Confirm_OldestFirst()
    { var (w, _) = Make(); w.Arm(2); w.Tick(); w.Arm(0); Assert.Equal(1, w.Confirm(1)); Assert.False(w.IsArmed(2)); Assert.True(w.IsArmed(0)); }

    [Fact] public void Confirm_Ties_LowestSlotFirst()
    { var (w, _) = Make(); w.Arm(3); w.Arm(1); w.Confirm(1); Assert.False(w.IsArmed(1)); Assert.True(w.IsArmed(3)); }

    [Fact] public void Confirm_CountCapsAtArmed()
    { var (w, _) = Make(); w.Arm(0); w.Arm(1); Assert.Equal(2, w.Confirm(5)); Assert.Equal(0, w.Confirm(1)); }

    [Fact] public void Disarm_And_Clear_AreSilent()
    { var (w, log) = Make(); w.Arm(0); w.Disarm(0); w.Arm(1); w.Clear(); Assert.False(w.IsArmed(0)); Assert.False(w.IsArmed(1)); Assert.Empty(log); }

    [Fact] public void NullTrace_DoesNotThrow()
    { var w = new ConfirmationWindow(12, 2, "T"); w.Arm(0); w.Confirm(1); w.Arm(1); w.Tick(); w.Tick(); w.Tick(); }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~ConfirmationWindowTests"`
Expected: build error, `ConfirmationWindow` not found.

- [ ] **Step 3: Implement `ConfirmationWindow` per the Interfaces block**

`int[] remaining` sized `slotCount`. `Confirm` loops `count` times picking the armed slot with the smallest `remaining` (strict `<`, scanning from slot 0, so ties go to the lowest index), zeroes it, traces, stops when none armed.

- [ ] **Step 4: Run to verify they pass**

Same command. Expected: 9 passed.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/ConfirmationWindow.cs test/SMWCounters.Tests/ConfirmationWindowTests.cs
git commit -m "feat: ConfirmationWindow — shared despawn-then-confirm window with trace"
```

---

### Task 2: `SpriteSlotTracker` shared class

**Files:**
- Create: `src/SMWCounters/Counters/SpriteSlotTracker.cs`
- Test: `test/SMWCounters.Tests/SpriteSlotTrackerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal sealed class SpriteSlotTracker
  {
      public const int SlotCount = 12;              // $14C8..$14D3 status, $9E..$A9 number
      public void Read(ISnesMemory memory);         // reads all 12 pairs; a slot with either read failing is !Ok and its previous is cleared
      public bool Ok(int slot);
      public byte Status(int slot);
      public byte Sprite(int slot);
      public bool HasPrevious(int slot);
      public byte PreviousStatus(int slot);
      public byte PreviousSprite(int slot);
      public void Commit();                         // previous := current for every Ok slot
      public void ClearSlot(int slot);              // forget previous for one slot
      public void Clear();                          // forget all previous
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
public class SpriteSlotTrackerTests
{
    private const int StatusBase = 0x14C8, SpriteBase = 0x009E;
    private static void Set(FakeSnesMemory m, int slot, byte status, byte sprite)
    { m.SetByte(StatusBase + slot, status); m.SetByte(SpriteBase + slot, sprite); }

    [Fact] public void FirstRead_IsOk_HasNoPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); t.Read(m);
      Assert.True(t.Ok(0)); Assert.Equal(0x08, t.Status(0)); Assert.Equal(0x78, t.Sprite(0)); Assert.False(t.HasPrevious(0)); }

    [Fact] public void Commit_ThenRead_ExposesPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); t.Read(m); t.Commit();
      Set(m, 0, 0x00, 0x78); t.Read(m);
      Assert.True(t.HasPrevious(0)); Assert.Equal(0x08, t.PreviousStatus(0)); Assert.Equal(0x78, t.PreviousSprite(0)); Assert.Equal(0x00, t.Status(0)); }

    [Fact] public void UnreadableSlot_IsNotOk_AndForgetsPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); t.Read(m); t.Commit();
      var empty = new FakeSnesMemory(); t.Read(empty);
      Assert.False(t.Ok(0)); Assert.False(t.HasPrevious(0)); Assert.False(t.Ok(5)); }

    [Fact] public void StatusOnly_IsNotOk()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); m.SetByte(StatusBase, 0x08); t.Read(m); Assert.False(t.Ok(0)); }

    [Fact] public void ClearSlot_And_Clear_ForgetPrevious()
    { var t = new SpriteSlotTracker(); var m = new FakeSnesMemory(); Set(m, 0, 0x08, 0x78); Set(m, 1, 0x08, 0x0F); t.Read(m); t.Commit();
      t.ClearSlot(0); Assert.False(t.HasPrevious(0)); Assert.True(t.HasPrevious(1)); t.Clear(); Assert.False(t.HasPrevious(1)); }
}
```

- [ ] **Step 2: Run to verify they fail** — filter `SpriteSlotTrackerTests`; expected build error.

- [ ] **Step 3: Implement `SpriteSlotTracker`** — arrays `byte[] status, sprite; bool[] ok; PreviousByte[] prev*` (reuse `PreviousByte`). `Read` does both reads per slot and calls `ClearSlot` when either fails.

- [ ] **Step 4: Run to verify they pass** — expected 5 passed.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/SpriteSlotTracker.cs test/SMWCounters.Tests/SpriteSlotTrackerTests.cs
git commit -m "feat: SpriteSlotTracker — shared per-poll sprite-table snapshot with previous values"
```

---

### Task 3: `KillCounter` adopts the shared pieces (behavior-preserving)

**Files:**
- Create: `src/SMWCounters/Counters/ITraceSource.cs`
- Modify: `src/SMWCounters/Counters/KillCounter.cs` (fields at ~L70–105, `ClearInFlightEvidence`, `ScanSprites`, `PollSlot`, `ClearSlot`, `ClearAll`)
- Test: `test/SMWCounters.Tests/KillCounterTests.cs` (unchanged, must pass) plus one new test appended

**Interfaces:**
- Consumes: `ConfirmationWindow` (Task 1), `SpriteSlotTracker` (Task 2).
- Produces:
  ```csharp
  internal interface ITraceSource { Action<string> Trace { get; set; } }
  ```
  `KillCounter : ISmwCounter, IBankToggleCounter, ITraceSource`; its `Trace` setter forwards to `coinWindow.Trace`. Trace tag `"COIN"`.

- [ ] **Step 1: Write the failing test** (append to `KillCounterTests`)

```csharp
[Fact]
public void CoinWindow_TracesConfirmation()
{
    var c = new KillCounter(); var m = new FakeSnesMemory(); var log = new List<string>();
    ((ITraceSource)c).Trace = log.Add;
    PollSlot0Coins(c, m, Alive, Galoomba, coins: 10);
    PollSlot0Coins(c, m, Alive, MovingCoin, coins: 10);
    PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 10);
    PollSlot0Coins(c, m, 0x00, MovingCoin, coins: 11);
    Assert.Contains("COIN confirmed slot0 polls=1", log);
}
```

- [ ] **Step 2: Run to verify it fails** — filter `KillCounterTests`; expected build error (`ITraceSource`).

- [ ] **Step 3: Refactor `KillCounter`**

Replace `prevStatus`/`prevSprite`/`statusBuf`/`spriteBuf`/`okBuf` with `private readonly SpriteSlotTracker slots = new();` and `int[] coinWindow` with `private readonly ConfirmationWindow coinWindow = new(SlotCount, CoinKillWindowPolls, "COIN");`. In `ScanSprites`: `slots.Read(memory)`; the tongue-marking loop reads `slots.Ok/Sprite/Status`; per slot `if (!slots.Ok(i)) { ClearSlot(i); continue; } PollSlot(i, slots.Status(i), slots.Sprite(i));`; after the loop `slots.Commit();`; then `if (coinsChanged && coinWindow.Confirm(1) > 0) { AddKill(); }` and `coinWindow.Tick();` (keep the `tongueLinger` decrement loop). In `PollSlot`: `if (!slots.HasPrevious(i)) return;` replaces the set-and-return; `prevStat/prevSpr` come from `slots.PreviousStatus/PreviousSprite`; E7 arms with `coinWindow.Arm(i)`; delete the trailing `pStat.Set/pSpr.Set`. `ClearSlot(i)`: `slots.ClearSlot(i)` plus evidence, `coinWindow.Disarm(i)`. `ClearInFlightEvidence`/`ClearAll`: `coinWindow.Clear()`, `slots.Clear()`. Remove the `SlotCount` const only if `SpriteSlotTracker.SlotCount` replaces every use.

- [ ] **Step 4: Run the FULL suite** — expected `Passed: 233` (218 + 9 + 5 + 1), zero failures, `KillCounterTests` untouched except the appended test.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/ITraceSource.cs src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/KillCounterTests.cs
git commit -m "refactor: KillCounter composes SpriteSlotTracker and ConfirmationWindow; COIN trace via ITraceSource"
```

---

### Task 4: `OneUpCounter`

**Files:**
- Create: `src/SMWCounters/Counters/OneUpCounter.cs`
- Test: `test/SMWCounters.Tests/OneUpCounterTests.cs`
- Test: `test/SMWCounters.Tests/CounterPersistenceTests.cs` (one test appended)

**Interfaces:**
- Consumes: `BankedCounter`, `MidwayExitBankDetector`, `SpriteSlotTracker`, `ConfirmationWindow`, `ITraceSource`, `IconLoader`.
- Produces:
  ```csharp
  internal sealed class OneUpCounter : BankedCounter, ITraceSource
  {
      public const int ConfirmWindowPolls = 3;
      public override string Id => "oneups";
      public override string DefaultLabel => "1-Ups";
      protected override string SaveName => "OneUps";
      public override Image DefaultIcon => icon;   // IconLoader.Load("LiveSplit.SmwCounters.Assets.oneup.png"); null until Task 5 embeds it
      public Action<string> Trace { get; set; }    // also forwarded to the window
  }
  ```
  Constants: `GameModeOffset 0x0100`, `LevelMainMode 0x14`, `ScoreSpriteBase 0x16E1`, `ScoreSpriteSlots 6`, `LakituFishingBase 0x151C`, `LoadIndexBase 0x161A`, `RiseTimerBase 0x1540`, `ReserveDropBase 0x1534`, `OneUpMushroom 0x78`, `FlyingGoldenMushroom 0x7F`, `Lakitu 0x1E`, `FirstLifeText 0x0D`, `LastLifeText 0x10`, `MinCandidateStatus 0x07`.

  `DetectCollectDelta(ISnesMemory memory)`, in this order:
  1. Gate: `$0100 != 0x14` → `ClearDetectors()` (clears slots, window, score-sprite previous, Lakitu previous, bank) and return 0.
  2. Read `$16E1..$16E6` into `PreviousByte[6]` tracking: `n` = count of slots with a previous value where current is in `0x0D..0x10` and `current != previous`. Unreadable slot → clear its previous.
  3. `slots.Read(memory)`. For each slot `i`:
     - `!Ok(i)` → `lakituPrev[i].Clear(); window.Disarm(i); continue`.
     - If `HasPrevious(i)`: `ps = PreviousSprite(i), pt = PreviousStatus(i), s = Sprite(i), t = Status(i)`.
       - **Mushroom vanish:** `(ps == 0x78 || ps == 0x7F) && pt >= 0x07 && (t == 0x00 || s != ps)` → trace `$"1UP candidate slot{i} #{ps:X2} from={pt:X2} idx={Hex($161A+i)} rise={Hex($1540+i)} drop={Hex($1534+i)}"` and `window.Arm(i)`.
       - **Lakitu bait:** `ps == 0x1E && pt == 0x08 && s == 0x1E && lakituPrev[i].HasPrevious && lakituPrev[i].Value != 0 && cur151C == 0` → trace `$"1UP candidate slot{i} lakitu idx=… rise=… drop=…"` and `window.Arm(i)`.
     - Lakitu previous upkeep: when `s == 0x1E` read `$151C+i` into `lakituPrev[i]` (clear on read failure); otherwise `lakituPrev[i].Clear()`.
  4. `slots.Commit()`.
  5. `int confirmed = window.Confirm(n); window.Tick(); return confirmed;`
  `DetectBank` → `MidwayExitBankDetector.DetectBank`. `ClearDetectors` clears everything above. `Hex` is a private helper returning `"??"` on a failed read.

- [ ] **Step 1: Write the failing tests**

```csharp
public class OneUpCounterTests
{
    private const int GameMode = 0x0100, StatusBase = 0x14C8, SpriteBase = 0x009E, Score = 0x16E1,
                      Lakitu151C = 0x151C, Anim = 0x0071, Midway = 0x13CE;
    private const byte Level = 0x14, Overworld = 0x0E, Alive = 0x08, Mouth = 0x07, Goal = 0x0C, Gone = 0x00,
                       OneUp = 0x78, Golden = 0x7F, Lakitu = 0x1E, Galoomba = 0x0F, Text1Up = 0x0D, Text2Up = 0x0E, NoText = 0x00;

    // Slot 0 plus score-sprite slot 0; other slots unset (reads fail).
    private static void Poll(OneUpCounter c, FakeSnesMemory m, byte status, byte sprite, byte score,
                             byte mode = Level, byte anim = 0, byte midway = 0)
    {
        m.SetByte(GameMode, mode); m.SetByte(StatusBase, status); m.SetByte(SpriteBase, sprite);
        m.SetByte(Score, score); m.SetByte(Anim, anim); m.SetByte(Midway, midway);
        c.Poll(m);
    }

    [Fact] public void Collect_ScoreSpriteSamePoll_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Equal(1, c.Value); Assert.True(c.ValueIsAlert); }

    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Collect_ScoreSpriteUpToThreePollsLate_Counts(int late)
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText);
      for (int i = 1; i < late; i++) { Poll(c, m, Gone, OneUp, NoText); }
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void Collect_ScoreSpriteFourPollsLate_DoesNotCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText);
      for (int i = 0; i < 3; i++) { Poll(c, m, Gone, OneUp, NoText); }
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void Vanish_NoScoreSprite_NoCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText);
      Assert.Equal(0, c.Value); }

    [Fact] public void ScoreSprite_NoVanish_ThenLaterVanish_NoCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Alive, OneUp, Text1Up);   // e.g. fifth Dragon Coin
      Poll(c, m, Alive, OneUp, Text1Up); Poll(c, m, Gone, OneUp, Text1Up);   // mushroom scrolls off, text unchanged
      Assert.Equal(0, c.Value); }

    [Fact] public void FlyingGoldenMushroom_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, Golden, NoText); Poll(c, m, Gone, Golden, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void GoalTapeStatus0C_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Goal, OneUp, NoText); Poll(c, m, Gone, OneUp, Text2Up); Assert.Equal(1, c.Value); }

    [Fact] public void MouthStatus_SwallowWithText_Counts_WithoutText_DoesNot()
    { var a = new OneUpCounter(); var ma = new FakeSnesMemory();
      Poll(a, ma, Mouth, OneUp, NoText); Poll(a, ma, Gone, OneUp, Text1Up); Assert.Equal(1, a.Value);
      var b = new OneUpCounter(); var mb = new FakeSnesMemory();
      Poll(b, mb, Mouth, OneUp, NoText); Poll(b, mb, Gone, OneUp, NoText); Poll(b, mb, Gone, OneUp, NoText); Assert.Equal(0, b.Value); }

    [Fact] public void DeadSetTransition_IsNotACandidate()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, 0x05, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void SlotReusedInsideOneGap_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Alive, Galoomba, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void Lakitu_FishingFlagDrops_WithText_Counts()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(Lakitu151C, 0x10); Poll(c, m, Alive, Lakitu, NoText);
      m.SetByte(Lakitu151C, 0x00); Poll(c, m, Alive, Lakitu, Text1Up); Assert.Equal(1, c.Value); }

    [Fact] public void Lakitu_FishingFlagDrops_NoText_NoCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(Lakitu151C, 0x10); Poll(c, m, Alive, Lakitu, NoText);
      m.SetByte(Lakitu151C, 0x00); Poll(c, m, Alive, Lakitu, NoText);
      for (int i = 0; i < 4; i++) { Poll(c, m, Alive, Lakitu, NoText); } Assert.Equal(0, c.Value); }

    [Fact] public void Lakitu_FlagAlreadyZero_NoCandidate()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(Lakitu151C, 0x00); Poll(c, m, Alive, Lakitu, NoText); Poll(c, m, Alive, Lakitu, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void TwoVanishes_TwoTexts_SamePoll_CountsTwo()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      m.SetByte(StatusBase + 1, Alive); m.SetByte(SpriteBase + 1, OneUp); m.SetByte(Score + 1, NoText);
      Poll(c, m, Alive, OneUp, NoText);
      m.SetByte(StatusBase + 1, Gone); m.SetByte(Score + 1, Text1Up);
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(2, c.Value); }

    [Fact] public void ConsecutiveCollects_EachCount()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Poll(c, m, Alive, OneUp, Text1Up); Poll(c, m, Gone, OneUp, Text2Up);   // slot reused by the LRU text table
      Assert.Equal(2, c.Value); }

    [Fact] public void GameModeGate_DropsArmedWindow()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, NoText, mode: Overworld);
      Poll(c, m, Gone, OneUp, NoText, mode: Level); Poll(c, m, Gone, OneUp, Text1Up, mode: Level); Assert.Equal(0, c.Value); }

    [Fact] public void ReadFailure_ThenReturn_NoCandidate()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText);
      var blank = new FakeSnesMemory(); blank.SetByte(GameMode, Level); c.Poll(blank);   // slot unreadable
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void Detach_ClearsState()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); m.Attached = false; c.Poll(m); m.Attached = true;
      Poll(c, m, Gone, OneUp, Text1Up); Assert.Equal(0, c.Value); }

    [Fact] public void DiscardOff_DeathKeepsTheCount()
    { var c = new OneUpCounter { Banked = false }; var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Equal(1, c.Value); Assert.False(c.ValueIsAlert);
      Poll(c, m, Gone, OneUp, Text1Up, anim: 9); Assert.Equal(1, c.Value); }

    [Fact] public void DiscardOn_DeathReverts_MidwayBanks()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory();
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up); Assert.True(c.ValueIsAlert);
      Poll(c, m, Gone, OneUp, Text1Up, anim: 9); Assert.Equal(0, c.Value);
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text2Up); Poll(c, m, Gone, OneUp, Text2Up, midway: 1);
      Assert.False(c.ValueIsAlert); Poll(c, m, Gone, OneUp, Text2Up, anim: 9); Assert.Equal(1, c.Value); }

    [Fact] public void Trace_EmitsCandidateAndConfirmation()
    { var c = new OneUpCounter(); var m = new FakeSnesMemory(); var log = new List<string>();
      ((ITraceSource)c).Trace = log.Add;
      Poll(c, m, Alive, OneUp, NoText); Poll(c, m, Gone, OneUp, Text1Up);
      Assert.Contains(log, l => l.StartsWith("1UP candidate slot0 #78 from=08"));
      Assert.Contains("1UP confirmed slot0 polls=0", log); }
}
```

Append to `CounterPersistenceTests`:

```csharp
[Fact]
public void OneUps_UnbankedTotal_SurvivesRoundTrip()
{
    var c = new OneUpCounter(); var m = new FakeSnesMemory();
    m.SetByte(0x0100, 0x14); m.SetByte(0x14C8, 0x08); m.SetByte(0x009E, 0x78); m.SetByte(0x16E1, 0x00); c.Poll(m);
    m.SetByte(0x14C8, 0x00); m.SetByte(0x16E1, 0x0D); c.Poll(m);
    var restored = new OneUpCounter(); XmlElement el = RoundTrip(c, restored);
    Assert.NotNull(el["OneUps"]); Assert.NotNull(el["OneUpsSaved"]); Assert.NotNull(el["OneUpsPlain"]);
    Assert.Equal(1, restored.Value); Assert.True(restored.ValueIsAlert);
    Assert.Equal(((ISmwCounter)c).StateHash, ((ISmwCounter)restored).StateHash);
}
```

- [ ] **Step 2: Run to verify they fail** — filter `OneUpCounterTests`; expected build error.

- [ ] **Step 3: Implement `OneUpCounter` per the Interfaces block**

Class comment states the rule in one paragraph and cites the spec §3 facts it rests on (score sprite written in the collect frame; Lakitu `$151C` = X-low & `$10`). `Trace` setter stores the sink and sets `window.Trace`.

- [ ] **Step 4: Run the FULL suite** — expected `Passed: 257` (233 + 23 + 1).

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/OneUpCounter.cs test/SMWCounters.Tests/OneUpCounterTests.cs test/SMWCounters.Tests/CounterPersistenceTests.cs
git commit -m "feat: OneUpCounter — collected 1-Up sprites and Lakitu's bait, confirmed by the 1UP score sprite"
```

---

### Task 5: Ship the counter — registration, icon, default-off toggle, README row

**Files:**
- Create: `src/SMWCounters/Assets/oneup.png` (placeholder: a copy of `mushroom.png`, same 16×16 treatment, until the author's 1-Up sprite lands)
- Modify: `src/SMWCounters/SMWCounters.csproj` (EmbeddedResource group), `src/SMWCounters/UI/Components/SmwCountersComponent.cs:82-92` (counter list), `src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs:524` (`DefaultBankOnSave`), `CREDITS.md`, `README.md` (counters table, "Discard on death" paragraph)
- Test: `test/SMWCounters.Tests/SettingsHashTests.cs`, `test/SMWCounters.Tests/OneUpCounterTests.cs`

- [ ] **Step 1: Write the failing tests**

In `SettingsHashTests`, rename `DefaultBankOnSave_MoonsOff_EverythingElseOn` to `DefaultBankOnSave_MoonsAndOneUpsOff_EverythingElseOn` and add `Assert.False(SmwCountersComponentSettings.DefaultBankOnSave("oneups"));`. In `OneUpCounterTests` add:

```csharp
[Fact] public void HasEmbeddedIcon() => Assert.NotNull(new OneUpCounter().DefaultIcon);
```

- [ ] **Step 2: Run to verify they fail** — filter `SettingsHashTests|OneUpCounterTests`; expected 2 failures.

- [ ] **Step 3: Wire it in**

`cp src/SMWCounters/Assets/mushroom.png src/SMWCounters/Assets/oneup.png`; csproj `<EmbeddedResource Include="Assets\oneup.png"><LogicalName>LiveSplit.SmwCounters.Assets.oneup.png</LogicalName></EmbeddedResource>`; `new OneUpCounter()` after `new DragonCoinCounter()`; `DefaultBankOnSave => counterId != "moons" && counterId != "oneups"` with the comment extended ("a respawning mushroom re-collected after a death would double count, so the plain history is the true-100% view; the toggle is the low% out"); CREDITS: `oneup.png` line noting it is a placeholder copy of the Super Mushroom sprite pending the 1-Up sprite; README table row after Dragon Coins: `| **1-Ups** | 1-Up mushrooms collected, including the flying golden mushroom and Fishin' Lakitu's bait. Lives from 100 coins, Dragon Coins, moons, bounce chains, and the bonus game do not count. The goal-tape 1-Up does. | hidden |`; README "Discard on death" paragraph: add 1-Ups to the banked list and change "It defaults on for everything except Moons." to "…except Moons and 1-Ups."

- [ ] **Step 4: Run the FULL suite** — expected `Passed: 258`.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Assets/oneup.png src/SMWCounters/SMWCounters.csproj src/SMWCounters/UI/Components/SmwCountersComponent.cs src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs CREDITS.md README.md test/SMWCounters.Tests/SettingsHashTests.cs test/SMWCounters.Tests/OneUpCounterTests.cs
git commit -m "feat: 1-Ups counter in the component — placeholder icon, discard-on-death off by default, README row"
```

---

### Task 6: Adjust hotkeys (+1 / −1 on one chosen counter) and ended-display re-capture

**Files:**
- Create: `src/SMWCounters/Counters/CounterAdjust.cs`
- Modify: `src/SMWCounters/UI/Components/EndedDisplayFreeze.cs`, `src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs` (properties ~L32, `BuildUi` after the Reset hotkey row ~L176–191, `RefreshFromModel` ~L391, `RegisterHotKeys` ~L478, `SetSettings` ~L537, `CreateSettingsNode` ~L592), `src/SMWCounters/UI/Components/SmwCountersComponent.cs` (`Hook_KeyOrButtonPressed` ~L183), `README.md` (Settings list)
- Test: `test/SMWCounters.Tests/CounterAdjustTests.cs`, `test/SMWCounters.Tests/EndedDisplayFreezeTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal static class CounterAdjust
  {
      public static ISmwCounter Find(IReadOnlyList<ISmwCounter> counters, string id);   // null when no counter has that Id
      public static void Apply(ISmwCounter counter, int delta);                         // counter.SetValue(Math.Max(0, counter.Value + delta))
  }
  // EndedDisplayFreeze
  public void Recapture(ISmwCounter counter);   // while ended: frozen[counter.Id] = (counter.Value, counter.ValueIsAlert); otherwise no-op
  // Settings
  public KeyOrButton IncrementKey { get; set; }   // default null
  public KeyOrButton DecrementKey { get; set; }   // default null
  public string HotkeyTarget { get; set; } = "oneups";
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

Append to `EndedDisplayFreezeTests`:

```csharp
[Fact]
public void Recapture_WhileEnded_ShowsTheAdjustedValue()
{
    var c = new StubCounter { Value = 10 }; var freeze = new EndedDisplayFreeze();
    freeze.OnPhase(isEnded: true, Counters(c));
    c.Value = 11;                       // live tally moved under the freeze
    Assert.Equal(10, freeze.ValueFor(c));
    freeze.Recapture(c);                // hotkey correction
    Assert.Equal(11, freeze.ValueFor(c));
}

[Fact]
public void Recapture_WhenNotEnded_IsANoOp()
{
    var c = new StubCounter { Value = 3 }; var freeze = new EndedDisplayFreeze();
    freeze.OnPhase(isEnded: false, Counters(c));
    freeze.Recapture(c); c.Value = 4;
    Assert.Equal(4, freeze.ValueFor(c));
}
```

- [ ] **Step 2: Run to verify they fail** — filter `CounterAdjustTests|EndedDisplayFreezeTests`; expected build errors.

- [ ] **Step 3: Implement `CounterAdjust`, `Recapture`, the settings, and the dispatch**

Settings `BuildUi`, directly after the Reset hotkey row (same geometry: label at `x=10`, control at `x=120` width 220, `y += 30` per row): "Adjust +1 hotkey (global):" text box bound through `CaptureKey(box, k => IncrementKey = k)`; "Adjust −1 hotkey (global):" likewise for `DecrementKey`; "Hotkeys adjust:" `ComboBox` (`DropDownStyle = DropDownList`, items are `(Id, Label)` pairs from the `counters` argument, `DisplayMember = "Label"`, `ValueMember = "Id"`, `SelectedValue = HotkeyTarget`, `SelectedValueChanged` → `HotkeyTarget = (string)SelectedValue`). `RefreshFromModel` updates all three; `RegisterHotKeys` registers both keys when non-null; `SetSettings` parses `IncrementKey`/`DecrementKey` like `ResetKey` and `HotkeyTarget = string.IsNullOrEmpty(e["HotkeyTarget"]?.InnerText) ? "oneups" : e["HotkeyTarget"].InnerText`; `CreateSettingsNode` adds the three `CreateSetting` calls (hash included). Component `Hook_KeyOrButtonPressed`: `else if (e == Settings.IncrementKey) Adjust(+1); else if (e == Settings.DecrementKey) Adjust(-1);` where `Adjust(int delta)` runs on the UI thread (`Settings.IsHandleCreated && Settings.InvokeRequired ? Settings.BeginInvoke(...) : direct`): `var c = CounterAdjust.Find(counters, Settings.HotkeyTarget); if (c == null) return; CounterAdjust.Apply(c, delta); endedFreeze.Recapture(c); Settings.RefreshValueBoxes();`. README Settings list gains: "**Adjust hotkeys** — optional +1 and −1 global keys that nudge one counter, chosen in **Hotkeys adjust** (default 1-Ups), for the rare miscount; they work while paused and after the final split, and a nudge is never discarded by a death."

- [ ] **Step 4: Run the FULL suite** — expected `Passed: 264` (258 + 4 + 2).

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/CounterAdjust.cs src/SMWCounters/UI/Components/EndedDisplayFreeze.cs src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs src/SMWCounters/UI/Components/SmwCountersComponent.cs README.md test/SMWCounters.Tests/CounterAdjustTests.cs test/SMWCounters.Tests/EndedDisplayFreezeTests.cs
git commit -m "feat: adjust hotkeys — +1/-1 on one chosen counter, banked immediately, visible after the final split"
```

---

### Task 7: Debug log — `lives=`, `SCR` and `LAK` traces, trace sink wiring

**Files:**
- Modify: `src/SMWCounters/Diagnostics/DebugLogger.cs` (header comment, context constants ~L55–64, `Poll`, `Idle`, new methods), `src/SMWCounters/UI/Components/SmwCountersComponent.cs` (`Poll`, next to the Banked sync loop ~L240)

**Interfaces:**
- Consumes: `ITraceSource` (Task 3).
- Produces: `public void Trace(string line)` on `DebugLogger` (forwards to `Write`). Line kinds added to the header comment: `SCR slot<n> <old>-><new> | mode=..` (`$16E1`–`$16E6` changes, previous known), `LAK slot<n> 151C <old>-><new> | mode=..` (for slots whose `$9E` is `1E`; previous cleared when the slot stops being Lakitu), `1UP …` and `COIN …` (written by the counters through the sink: candidate, confirmed, expired). `CTR` context gains `lives=` (`$0DBE`).

- [ ] **Step 1: Implement**

`DebugLogger`: `PlayerLives = 0x0DBE` in the `CTR` ctx string; `ScoreSpriteBase = 0x16E1`, `ScoreSpriteSlots = 6`, `PreviousByte[6]` + `LogScoreSprites(mem)`; `LakituFishingBase = 0x151C`, `PreviousByte[12]` + `LogLakituFishing(mem)`; both called from `Poll`, both cleared in `Idle`. Component `Poll`, every tick next to the Banked sync: `foreach (ISmwCounter c in counters) if (c is ITraceSource t) t.Trace = Settings.DebugLog ? debugLog.Trace : null;` (an `Action<string>` method-group assignment each tick allocates a delegate; cache one `Action<string> traceSink = debugLog.Trace` field and assign that or null).

- [ ] **Step 2: Build and run the FULL suite** — `dotnet build src/SMWCounters/SMWCounters.csproj -c Debug --nologo` clean; test suite `Passed: 264`.

- [ ] **Step 3: Commit**

```bash
git add src/SMWCounters/Diagnostics/DebugLogger.cs src/SMWCounters/UI/Components/SmwCountersComponent.cs
git commit -m "feat: debug log traces score sprites, Lakitu's fishing flag, lives, and the counters' 1UP/COIN windows"
```

---

### Task 8: BACKLOG — live pass owed, icon placeholder, sprite-identity follow-up

**Files:**
- Modify: `docs/superpowers/BACKLOG.md`

- [ ] **Step 1: Edit the backlog**

Under **Now**, after the Dragon Coins entry, add **"1-Ups: shipped on unit tests 2026-10-24, live pass owed."** with the spec §8 list verbatim (first check: a `SCR` 1UP-family entry appears on the first collect; then placed, block, flying golden, Fishin' Lakitu in Forest of Illusion 4, Yoshi tongue and swallow, goal-tape 1-Up counts, bonus game does not and shows no `SPR` `$78` line, 100 coins / fifth Dragon Coin / moon / bounce chain none, scroll-off none, collect-die-recollect with discard off and on, hotkeys paused / running / after the final split; read `1UP … polls=` to confirm `W = 3`). Add **"1-Ups icon is a placeholder"** (copy of `mushroom.png`; swap in the author's 16×16 1-Up sprite as `Assets/oneup.png`, no code change, update CREDITS). Under **Later — medium**, add **"Sprite identity machinery"** summarizing spec §9 (identity `(room $010B, $161A)` for level-placed sprites, `$1540`/`$1534` zero at first sighting, commit identities when the count commits; candidates: 1-Ups dedupe, Yoshi-as-powerup; gate: the 1-Ups live pass's `1UP candidate … idx= rise= drop=` lines across a death). Absolute dates only, no relative phrasing.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/BACKLOG.md
git commit -m "docs: backlog — 1-Ups live pass owed, placeholder icon, sprite-identity follow-up"
```

---

## Self-review notes

- Spec coverage: §4 rule → Task 4; shared pieces → Tasks 1–3; §5 counter/banking/icon → Tasks 4–5; §6 hotkeys and re-capture → Task 6; §7 log → Tasks 1, 3, 4 (sink + lines) and 7 (logger); §8 → Task 8; §9 → Task 8 entry; §3 facts are cited in `OneUpCounter`'s class comment.
- Names used consistently: `ConfirmationWindow.{Arm,IsArmed,Disarm,Confirm,Tick,Clear,Trace,WindowPolls}`, `SpriteSlotTracker.{Read,Ok,Status,Sprite,HasPrevious,PreviousStatus,PreviousSprite,Commit,ClearSlot,Clear}`, `ITraceSource.Trace`, `OneUpCounter.ConfirmWindowPolls`, `CounterAdjust.{Find,Apply}`, `EndedDisplayFreeze.Recapture`, settings `IncrementKey`/`DecrementKey`/`HotkeyTarget`, `DebugLogger.Trace`.
- Test counts assume no test is removed; adjust the expected totals if a task adds a `Theory` case.
