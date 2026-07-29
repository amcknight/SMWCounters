# Persistence-Hash Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every piece of counter/settings state that `SaveState`/`CreateSettingsNode` persists actually dirty LiveSplit's layout hash, so no user change can silently drop on save.

**Architecture:** Add a `StateHash` member to `ISmwCounter` (hash of everything `SaveState` writes) so the component folds full counter state without downcasts; replace both unsalted XOR accumulators (`SmwCountersComponent.GetSettingsHashCode`, `SmwCountersComponentSettings.CreateSettingsNode`) with order/category-sensitive `hash * 397 ^ x` combines; extract the settings set-fold into a pure static so it is testable without constructing the WinForms control.

**Tech Stack:** C# (.NET Framework 4.8.1), xUnit, existing `FakeSnesMemory` test double.

**Spec:** `docs/superpowers/scans/2026-07-28-improve.md` → Top wins → must-fix, first bullet; verification details under V1/V2 in the same file.

## Global Constraints

- Work on the existing branch `improve/hash-integrity-and-cleanups` — do NOT create a new branch.
- `Id` strings and XML element names are stable serialization keys — must not change.
- Hash values are compared only within a session (LiveSplit polls `GetSettingsHashCode` to detect a dirty layout); cross-run stability is NOT required, so `string.GetHashCode()` is fine.
- Deliberate non-goal: `SetEnabled` does NOT prune `bankDisabled`. The discard-on-death preference must survive disable→re-enable of a counter; the set-salting in Task 3 removes the hash collision that stale ids caused, so pruning is unnecessary and would lose user preference.
- Test suite must stay green after every task: `dotnet test SMWCounters.sln -c Debug` (119 tests passing at branch head 8cd1762).
- Commit format: end commit messages with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: `StateHash` on `ISmwCounter` and all implementations

**Files:**
- Modify: `src/SMWCounters/Counters/ISmwCounter.cs` (add member after `ValueIsAlert`, line ~29)
- Modify: `src/SMWCounters/Counters/DeathCounter.cs` (~line 26)
- Modify: `src/SMWCounters/Counters/MoonCounter.cs` (~line 35)
- Modify: `src/SMWCounters/Counters/BankedCounter.cs` (~line 42)
- Modify: `src/SMWCounters/Counters/KillCounter.cs` (line 132: `internal` → `public`)
- Test: create `test/SMWCounters.Tests/StateHashTests.cs`

**Interfaces:**
- Consumes: existing counter classes; `FakeSnesMemory` (`SetByte(int, byte)` / `Attached`); `PowerupCounterTests`-style poll helper.
- Produces: `int StateHash { get; }` on `ISmwCounter` — hash of everything the counter's `SaveState` writes. Implementations: `DeathCounter.StateHash => Value`; `BankedCounter.StateHash => total * 397 ^ saved`; `MoonCounter.StateHash => Value * 397 ^ (int)DedupeMode`; `KillCounter.StateHash` keeps its existing formula `kills ^ (destruction * 397) ^ (int)Mode` but becomes `public`. Task 2 relies on `c.StateHash` compiling for every `ISmwCounter`.

- [ ] **Step 1: Write the failing tests**

Create `test/SMWCounters.Tests/StateHashTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build SMWCounters.sln -c Debug`
Expected: BUILD FAILURE — `'ISmwCounter' does not contain a definition for 'StateHash'` (a compile error is this step's "failing test").

- [ ] **Step 3: Add the interface member and implementations**

In `src/SMWCounters/Counters/ISmwCounter.cs`, after the `ValueIsAlert` block (line ~29):

```csharp
    // Hash of everything SaveState persists (value plus counter-specific
    // config/hidden tallies). The component folds this into LiveSplit's
    // layout hash so any persisted change dirties the layout — state that
    // skips this hash can silently drop on save.
    int StateHash { get; }
```

In `src/SMWCounters/Counters/DeathCounter.cs`, after `public bool ValueIsAlert => false;`:

```csharp
    public int StateHash => Value;
```

In `src/SMWCounters/Counters/BankedCounter.cs`, after `public bool ValueIsAlert => total != saved;`:

```csharp
    // saved is persisted but invisible to Value, so it must feed the hash:
    // a bank commit (saved = total) changes what SaveState writes without
    // moving Value at all.
    public int StateHash => total * 397 ^ saved;
```

In `src/SMWCounters/Counters/MoonCounter.cs`, after the `DedupeMode` property:

```csharp
    public int StateHash => Value * 397 ^ (int)DedupeMode;
```

In `src/SMWCounters/Counters/KillCounter.cs` line 132, change the access modifier only (formula unchanged):

```csharp
    public int StateHash => kills ^ (destruction * 397) ^ (int)Mode;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SMWCounters.sln -c Debug`
Expected: PASS — 124 tests (119 existing + 5 new).

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/ISmwCounter.cs src/SMWCounters/Counters/DeathCounter.cs src/SMWCounters/Counters/BankedCounter.cs src/SMWCounters/Counters/MoonCounter.cs src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/StateHashTests.cs
git commit -m "feat: StateHash on ISmwCounter — every counter hashes all persisted state

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Salted ordered fold in the component's layout hash

**Files:**
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs` — `GetSettingsHashCode()` (~line 449, the method containing `hash ^= c is KillCounter kc ? kc.StateHash : c.Value.GetHashCode();`)

**Interfaces:**
- Consumes: `ISmwCounter.StateHash` from Task 1.
- Produces: nothing new — `GetSettingsHashCode()` keeps its signature; the `KillCounter` downcast disappears.

- [ ] **Step 1: Replace the fold**

The old fold XORs raw ints (commutative and self-inverse: `{deaths:3, exits:3}` hashes like `{deaths:0, exits:0}`) and special-cases one counter. Replace the loop body and its comment:

```csharp
    public int GetSettingsHashCode()
    {
        int hash = Settings.GetSettingsHashCode();
        foreach (ISmwCounter c in counters)
        {
            // Salted, order-sensitive fold: raw XOR of counter values is
            // commutative and self-inverse, so two counters holding equal
            // values cancel out and the layout hash misses real changes.
            // StateHash (not Value) so persisted-but-hidden state — banked
            // `saved`, moon dedupe mode, the off-display kill tally — also
            // dirties the hash instead of silently dropping on save.
            hash = hash * 397 ^ c.StateHash;
        }
        return hash;
    }
```

(The `counters` array order is fixed at construction, so the ordered fold is deterministic.)

- [ ] **Step 2: Build and run the full suite**

Run: `dotnet test SMWCounters.sln -c Debug`
Expected: PASS — 124 tests. (The component itself has no direct tests; Task 1's tests pin the `StateHash` inputs this fold consumes.)

- [ ] **Step 3: Commit**

```bash
git add src/SMWCounters/UI/Components/SmwCountersComponent.cs
git commit -m "fix: layout hash — salted ordered fold over StateHash, no more equal-value cancellation

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Settings hash — distinguish the enabled and bankDisabled sets

**Files:**
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs` — tail of `CreateSettingsNode` (lines ~519-521: the two `foreach` XOR loops)
- Test: create `test/SMWCounters.Tests/SettingsHashTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (independent of Tasks 1-2).
- Produces: `internal static int CombineSetHashes(int hash, IEnumerable<string> enabledIds, IEnumerable<string> bankDisabledIds)` on `SmwCountersComponentSettings` — pure, callable from tests without constructing the WinForms control (`InternalsVisibleTo("SMWCounters.Tests")` already exists).

- [ ] **Step 1: Write the failing tests**

Create `test/SMWCounters.Tests/SettingsHashTests.cs`:

```csharp
using LiveSplit.UI.Components;
using Xunit;

namespace SMWCounters.Tests;

// The settings hash previously XORed enabled ids and bankDisabled ids into
// one accumulator, so an id present in both sets cancelled out:
// {enabled:[coins], bankDisabled:[coins]} hashed identically to {} and the
// user's change never dirtied the layout. These tests pin the salted fold.
// CombineSetHashes is static so no WinForms control (and no CompositeHook)
// is constructed in tests.
public class SettingsHashTests
{
    private static readonly string[] None = System.Array.Empty<string>();

    [Fact]
    public void IdInBothSets_DoesNotCancelToEmpty()
    {
        int both = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, new[] { "coins" });
        int neither = SmwCountersComponentSettings.CombineSetHashes(0, None, None);
        Assert.NotEqual(neither, both);
    }

    [Fact]
    public void SameId_DifferentSet_HashesDifferently()
    {
        int inEnabled = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "coins" }, None);
        int inBank = SmwCountersComponentSettings.CombineSetHashes(0, None, new[] { "coins" });
        Assert.NotEqual(inEnabled, inBank);
    }

    [Fact]
    public void TogglingBankDisabled_ChangesHash()
    {
        int on = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "deaths", "coins" }, None);
        int off = SmwCountersComponentSettings.CombineSetHashes(0, new[] { "deaths", "coins" }, new[] { "coins" });
        Assert.NotEqual(on, off);
    }

    [Fact]
    public void SetIterationOrder_DoesNotMatter()
    {
        // enabled/bankDisabled are HashSets — the hash must not depend on
        // iteration order, only on membership.
        int ab = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "a", "b" }, None);
        int ba = SmwCountersComponentSettings.CombineSetHashes(7, new[] { "b", "a" }, None);
        Assert.Equal(ab, ba);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build SMWCounters.sln -c Debug`
Expected: BUILD FAILURE — `'SmwCountersComponentSettings' does not contain a definition for 'CombineSetHashes'`.

- [ ] **Step 3: Extract and salt the set fold**

In `src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs`, replace the last three lines of `CreateSettingsNode`:

```csharp
        foreach (string id in enabled) { hash ^= id.GetHashCode(); }
        foreach (string id in bankDisabled) { hash ^= id.GetHashCode(); }
        return hash;
```

with:

```csharp
        return CombineSetHashes(hash, enabled, bankDisabled);
```

and add below `CreateSettingsNode`:

```csharp
    // Fold the two id sets into the settings hash. Each set folds
    // commutatively (HashSet iteration order is unspecified) into its own
    // sub-hash, then the sub-hashes combine order-sensitively — an id in
    // `enabled` can no longer cancel the same id in `bankDisabled`, which
    // previously made {enabled:[x], bankDisabled:[x]} hash like {}.
    // Static and internal so tests can pin the collision fix without
    // constructing this control (and its CompositeHook).
    internal static int CombineSetHashes(int hash, IEnumerable<string> enabledIds, IEnumerable<string> bankDisabledIds)
    {
        int enabledHash = 0;
        int bankHash = 0;
        foreach (string id in enabledIds) { enabledHash ^= id.GetHashCode(); }
        foreach (string id in bankDisabledIds) { bankHash ^= id.GetHashCode(); }
        hash = hash * 397 ^ enabledHash;
        return hash * 397 ^ bankHash;
    }
```

`System.Collections.Generic` is already imported in this file (it uses `HashSet<string>`); no new usings are needed.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SMWCounters.sln -c Debug`
Expected: PASS — 128 tests (124 + 4 new).

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/UI/Components/SmwCountersComponentSettings.cs test/SMWCounters.Tests/SettingsHashTests.cs
git commit -m "fix: settings hash — salt enabled/bankDisabled sets so shared ids can't cancel

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Persistence round-trip regression tests

**Files:**
- Test: create `test/SMWCounters.Tests/CounterPersistenceTests.cs`

**Interfaces:**
- Consumes: `ISmwCounter.SaveState(XmlDocument, XmlElement)` / `LoadState(XmlElement)` (existing); `StateHash` from Task 1; the `PowerupCounterTests`-style poll helper.
- Produces: nothing — pure regression coverage. The bank event was previously invisible to the hash, invisible to the debug log, AND untested at the persistence layer (scan file, skeptic M4); this task closes the third leg.

- [ ] **Step 1: Write the tests**

Create `test/SMWCounters.Tests/CounterPersistenceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the new tests**

Run: `dotnet test SMWCounters.sln -c Debug`
Expected: PASS — 131 tests (128 + 3 new). These should pass immediately (the persistence code was already correct; only the hash was blind). If any fail, that is a real pre-existing persistence bug — stop and surface it rather than adjusting the test.

- [ ] **Step 3: Commit**

```bash
git add test/SMWCounters.Tests/CounterPersistenceTests.cs
git commit -m "test: counter persistence round-trips — unbanked totals and moon dedupe survive save/load

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
