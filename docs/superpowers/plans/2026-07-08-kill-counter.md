# Kill Counter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `KillCounter` that tallies enemy kills by edge-detecting the SMW sprite-status table (`$14C8`), exposed as an optional, default-off counter row.

**Architecture:** A new plain `ISmwCounter` (modeled on `JumpCounter`, not `BankedCounter`) tracks each of the 12 sprite slots with a `PreviousByte` and increments once per live→dead-set transition, gated to level-main game mode. It is registered in `SmwCountersComponent`'s counter array, inheriting all existing settings/persistence/poll wiring.

**Tech Stack:** C# (.NET), LiveSplit component, xUnit tests via `FakeSnesMemory`.

## Global Constraints

- Counter must implement `LiveSplit.SmwCounters.Counters.ISmwCounter` exactly.
- `Id` is a stable serialization key and must be `"kills"` — never change once shipped.
- Dead-set is exactly `{0x02, 0x03, 0x04, 0x05}` for v1. `06`/`07`/`0C` are NOT counted.
- No sprite-ID filtering in v1 (P-switch etc. may count; that is intended).
- In-level gate: game mode `$0100 == 0x14` (`LevelMainMode`), matching `PowerupCounter`.
- Sprite-status table: base `0x14C8`, 12 slots (`0x14C8`–`0x14D3`).
- Default **disabled** (not added to the initial enabled set `{ "deaths", "exits" }`).
- Build: `dotnet build src/SMWCounters/SMWCounters.csproj -c Release`. Test: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`.

---

### Task 1: KillCounter class + unit tests

**Files:**
- Create: `src/SMWCounters/Counters/KillCounter.cs`
- Test: `test/SMWCounters.Tests/KillCounterTests.cs`

**Interfaces:**
- Consumes: `ISmwCounter` (interface), `ISnesMemory.ReadWramByte(int, out byte)` / `IsAttached`, `PreviousByte` (`.HasPrevious`, `.Value`, `.Set(byte)`, `.Clear()`), `LiveSplit.UI.SettingsHelper.CreateSetting` / `ParseInt`, `SMWCounters.Tests.FakeSnesMemory` (`.Attached`, `.SetByte(int, byte)`).
- Produces: `public sealed class KillCounter : ISmwCounter` with `Id => "kills"`, `DefaultLabel => "Kills"`, `DefaultIcon => icon` (embedded `Assets/kill.png`), `ValueIsAlert => false`, `int Value`, `void SetValue(int)`, `void Reset()`, `void Poll(ISnesMemory)`, `void SaveState(XmlDocument, XmlElement)`, `void LoadState(XmlElement)`.

- [ ] **Step 1: Write the failing tests**

Create `test/SMWCounters.Tests/KillCounterTests.cs`:

```csharp
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
    [InlineData((byte)0x06)]   // goal-tape coin
    [InlineData((byte)0x07)]   // Yoshi's mouth
    [InlineData((byte)0x0C)]   // goal-tape powerup
    public void NonDeadTransition_DoesNotCount(byte target)
    {
        var c = new KillCounter(); var m = new FakeSnesMemory();
        PollSlot0(c, m, Alive);
        PollSlot0(c, m, target);
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: FAIL to compile — `KillCounter` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/SMWCounters/Counters/KillCounter.cs`:

```csharp
using System.Drawing;
using System.Xml;

using LiveSplit.SmwCounters.Snes;
using LiveSplit.UI;

namespace LiveSplit.SmwCounters.Counters;

// Counts enemy kills by edge-detecting each sprite slot's status transitioning
// from a live state into the "dead" set. The 12-byte sprite-status table lives
// at $14C8 ($14C8..$14D3, one byte per slot).
//
//   $7E:0100 (GameMode):      $14 = level main routine. Gate to this so
//                             overworld / load-time garbage in the status table
//                             doesn't count.
//   $7E:14C8[i] (SpriteStatus): per-slot status. Dead set = 02 (killed, falling
//                             off screen), 03 (smushed), 04 (spinjumped),
//                             05 (lava/mud).
//
// Rule: count once per slot when the previous sample was NOT in the dead set and
// not empty ($00), and the current sample IS in the dead set. Treating the dead
// set as one absorbing region means shuffling within it (e.g. 04 -> 02) does not
// double-count, and offscreening (status -> 00) is naturally excluded.
//
// v1 deliberately does no sprite-ID filtering: a P-switch press can register as
// 03 and will count. This is an observation instrument; filtering (if any) waits
// on real-play data. Goal-tape conversions (06/0C) and Yoshi's mouth (07) are
// excluded only by virtue of not being in the dead set.
internal sealed class KillCounter : ISmwCounter
{
    private const int GameModeOffset = 0x0100;
    private const byte LevelMainMode = 0x14;
    private const int SpriteStatusBase = 0x14C8;
    private const int SlotCount = 12;

    private static readonly Bitmap icon = IconLoader.Load("LiveSplit.SmwCounters.Assets.kill.png");

    private readonly PreviousByte[] previousStatus;

    public KillCounter()
    {
        previousStatus = new PreviousByte[SlotCount];
        for (int i = 0; i < SlotCount; i++) { previousStatus[i] = new PreviousByte(); }
    }

    public string Id => "kills";
    public Image DefaultIcon => icon;
    public string DefaultLabel => "Kills";

    public int Value { get; private set; }

    public bool ValueIsAlert => false;

    public void Reset()
    {
        Value = 0;
        ClearAll();
    }

    public void SetValue(int value) => Value = value;

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
            if (!memory.ReadWramByte(SpriteStatusBase + i, out byte status))
            {
                previousStatus[i].Clear();
                continue;
            }

            PreviousByte prev = previousStatus[i];
            if (prev.HasPrevious && prev.Value != 0x00 && !IsDead(prev.Value) && IsDead(status))
            {
                Value++;
            }

            prev.Set(status);
        }
    }

    private static bool IsDead(byte status) => status >= 0x02 && status <= 0x05;

    private void ClearAll()
    {
        for (int i = 0; i < SlotCount; i++) { previousStatus[i].Clear(); }
    }

    public void SaveState(XmlDocument doc, XmlElement parent)
    {
        SettingsHelper.CreateSetting(doc, parent, "Kills", Value);
    }

    public void LoadState(XmlElement parent)
    {
        Value = SettingsHelper.ParseInt(parent["Kills"], 0);
        ClearAll();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: PASS — all `KillCounterTests` green, existing tests still green.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Counters/KillCounter.cs test/SMWCounters.Tests/KillCounterTests.cs
git commit -m "feat: KillCounter detecting sprite live->dead transitions"
```

---

### Task 2: Register KillCounter in the component

**Files:**
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs` (constructor `counters` array, ~lines 65-73)

**Interfaces:**
- Consumes: `KillCounter` from Task 1.
- Produces: no new API — the counter now appears in the component registry, so it renders a settings row and participates in polling/persistence automatically.

- [ ] **Step 1: Add KillCounter to the registry**

In the constructor of `SmwCountersComponent`, add `new KillCounter()` to the `counters` array. After the edit the block reads:

```csharp
        // Build the registry of known counters.
        var moon = new MoonCounter();
        counters = new ISmwCounter[]
        {
            new DeathCounter(),
            new ExitCounter(),
            moon,
            new JumpCounter(),
            new PowerupCounter(),
            new KillCounter(),
        };
```

No other change is needed: `BuildExtras` already returns `(null, null)` for any counter that is not `MoonCounter`/`PowerupCounter`, and the settings row, poll loop, timer-gate flush, reset wiring, and save/load loops all iterate this array. `KillCounter` is not in the settings' initial enabled set (`{ "deaths", "exits" }`), so it defaults off.

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/SMWCounters/SMWCounters.csproj -c Release`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj`
Expected: PASS — all tests green (nothing enumerates the registry, so the new counter breaks no existing assertion).

- [ ] **Step 4: Commit**

```bash
git add src/SMWCounters/UI/Components/SmwCountersComponent.cs
git commit -m "feat: register KillCounter (default-off Kills row)"
```

---

## Self-Review

**Spec coverage:**
- Core mechanic (live→dead-set, per-slot, absorbing region) → Task 1 impl + tests 1,2,3,5.
- Dead-set `{02,03,04,05}` only; `06/07/0C` excluded → Task 1 `IsDead` + test `NonDeadTransition_DoesNotCount`.
- In-level gate `$0100==0x14`, clear on gate-off/detach → Task 1 `Poll` + tests `GameModeGate...`, `Detach...`.
- Neutral display, `kill.png` icon, `Id="kills"`, default-off → Task 1 identity members + Task 2 registration (absent from enabled set).
- Persistence `<Kills>` → Task 1 `SaveState`/`LoadState`.
- Wiring inherits settings/poll/reset → Task 2.
- Deferred items (styles, checkboxes, filtering, `06/07`) → intentionally not in plan; documented in spec.

**Placeholder scan:** none — all steps contain full code/commands/expected output.

**Type consistency:** `IsDead`, `ClearAll`, `previousStatus`, `SlotCount`, offsets, and the `"kills"`/`"Kills"` strings are used consistently across impl and tests; `PollSlot0` helper signature matches usage.
