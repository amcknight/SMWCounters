# SNES.dll Integration + Status Dot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the ported kaizosplits offset tables with SNES.dll v1.6.0 structural WRAM discovery (zero-config, any emulator build) and add a status dot to the component row showing connection health.

**Architecture:** A new `SnesConnection` bridge class implements the existing `ISnesMemory` seam over `SNES.Emu`, driving the status-first consumer idiom once per poll tick; a pure `StatusDot.ColorFor` maps `EmuStatus` primitives to the dot color; a pure `StatusChangeFilter` implements the log-on-change idiom for the debug log. `SnesEmu.cs` and `Offsets.cs` are deleted. SNES.dll ships as a pinned binary in `lib/` and deploys/releases as a pair with SMWCounters.dll.

**Tech Stack:** C# / .NET Framework 4.8.1, WinForms (LiveSplit component), xUnit, SNES.dll v1.6.0 (netstandard2.0, from sibling repo `../snes_offsets`).

**Spec:** `docs/2026-07-27-snes-dll-integration-design.md` (committed). Consumer contract: `../snes_offsets/docs/status-first-consumption.md`.

## Global Constraints

- All work on branch `feat/snes-dll-discovery`; merge to `master` only after the live gate (Task 9) passes — the live gate itself requires Andrew.
- SNES.dll is pinned at **v1.6.0** (built from snes_offsets `d5777e6`); never reference snes_offsets source except via the optional `SnesSrcPath` dev switch.
- Never parse SNES.dll exception messages — exceptions are control flow; `Status()` is the telemetry API.
- The process-name list must mirror `../kaizosplits/Kaizo.asl` `state()` order exactly: `snes9x, snes9x-x64, bsnes, retroarch, higan, snes9x-rr, mesen, emuhawk, ares, mednafen`.
- Counters and `ISnesMemory` do not change. Existing tests must stay green and unmodified.
- Component version becomes **0.3.0**.
- This is a Windows machine: don't use `cd /d`; prefer absolute paths. LiveSplit must be CLOSED when deploying DLLs (it locks them; builds "succeed" but deploy silently stays stale).
- Commit after every task (repo grants standing commit authorization).

---

### Task 1: Branch + pinned SNES.dll in lib/

**Files:**
- Modify: `.gitignore`
- Create: `lib/SNES.dll` (binary, committed)

**Interfaces:**
- Produces: `lib/SNES.dll` v1.6.0 for Task 2's `<Reference>`.

- [ ] **Step 1: Create the branch**

```bash
cd "c:/Users/thedo/git/SMWCounters" && git checkout -b feat/snes-dll-discovery
```

- [ ] **Step 2: Build SNES.dll Release from the pinned snes_offsets commit**

snes_offsets `main` is at `d5777e6` (v1.6.0). Verify, then build (requires the sibling `c:/Users/thedo/git/LiveSplit` checkout, which exists on this machine; expect NU1701/NU1702/MSB3277 warnings — they are noise from the LiveSplit reference):

```bash
cd "c:/Users/thedo/git/snes_offsets" && git rev-parse --short HEAD   # expect d5777e6
dotnet build src/SNES/SNES.csproj -c Release
```

- [ ] **Step 3: Copy into lib/ and verify the version**

```bash
cp "c:/Users/thedo/git/snes_offsets/src/SNES/bin/Release/netstandard2.0/SNES.dll" "c:/Users/thedo/git/SMWCounters/lib/SNES.dll"
```

```powershell
(Get-Item "c:/Users/thedo/git/SMWCounters/lib/SNES.dll").VersionInfo.FileVersion
```

Expected: `1.6.0.0`. If not, the Release build is stale — rebuild and re-copy.

- [ ] **Step 4: Gitignore carve-out**

In `.gitignore`, replace:

```
# Fetched third-party assemblies (see scripts/fetch-livesplit-core.ps1)
lib/*.dll
```

with:

```
# Fetched third-party assemblies (see scripts/fetch-livesplit-core.ps1)
lib/*.dll
# ...except the pinned SNES.dll (structural WRAM discovery, from the
# snes_offsets project). Upgrading = rebuild snes_offsets Release, copy,
# commit the bump.
!lib/SNES.dll
```

- [ ] **Step 5: Commit**

```bash
cd "c:/Users/thedo/git/SMWCounters" && git add .gitignore lib/SNES.dll && git commit -m "feat: pin SNES.dll v1.6.0 in lib/ (structural WRAM discovery)"
```

---

### Task 2: csproj wiring — SNES reference, dev mode, deploy pair, version 0.3.0

**Files:**
- Modify: `src/SMWCounters/SMWCounters.csproj`

**Interfaces:**
- Consumes: `lib/SNES.dll` (Task 1).
- Produces: `SNES` namespace available to `src/SMWCounters` code (Tasks 5–7); `SNES.dll` copied to build output and to `$(ComponentsPath)`.

- [ ] **Step 1: Bump version**

In `src/SMWCounters/SMWCounters.csproj`, change `<Version>0.2.0</Version>` to `<Version>0.3.0</Version>`.

- [ ] **Step 2: Add the SNES reference (pinned DLL by default, source via SnesSrcPath)**

Insert after the existing LiveSplit.Core reference ItemGroups (after the `Condition="'$(LsSrcPath)' == ''"` ItemGroup closes):

```xml
  <!-- SNES.dll: structural WRAM discovery (snes_offsets project). Default is
       the pinned binary in lib/ (committed; see .gitignore carve-out).
       Private=true because LiveSplit does NOT provide this assembly — it must
       ship next to SMWCounters.dll. Set SnesSrcPath in the gitignored
       SMWCounters.local.props to build against ../snes_offsets source instead
       (requires the sibling LiveSplit checkout that SNES.csproj references). -->
  <ItemGroup Condition="'$(SnesSrcPath)' != ''">
    <ProjectReference Include="$(SnesSrcPath)\src\SNES\SNES.csproj" />
  </ItemGroup>
  <ItemGroup Condition="'$(SnesSrcPath)' == ''">
    <Reference Include="SNES">
      <HintPath>$(MSBuildThisFileDirectory)..\..\lib\SNES.dll</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
```

- [ ] **Step 3: Deploy the pair in CopyToLiveSplitComponents**

Replace the whole existing `CopyToLiveSplitComponents` target with:

```xml
  <Target Name="CopyToLiveSplitComponents" AfterTargets="Build" Condition="Exists('$(ComponentsPath)')">
    <!-- SMWCounters.dll and the SNES.dll it was built against deploy as a
         pair so LiveSplit can never load a mismatched set. ContinueOnError:
         a running LiveSplit locks loaded DLLs; warn (close it) instead of
         failing the build. Touch: stamp the copy time so a rebuild visibly
         updates the file. -->
    <ItemGroup>
      <_DeployPair Include="$(TargetPath);$(TargetDir)SNES.dll" />
    </ItemGroup>
    <Copy SourceFiles="@(_DeployPair)" DestinationFolder="$(ComponentsPath)" ContinueOnError="true">
      <Output TaskParameter="CopiedFiles" ItemName="_DeployedDll" />
    </Copy>
    <Copy SourceFiles="$(TargetDir)$(TargetName).pdb" DestinationFolder="$(ComponentsPath)" Condition="Exists('$(TargetDir)$(TargetName).pdb')" ContinueOnError="true" />
    <Touch Files="$(ComponentsPath)\$(TargetFileName)" ContinueOnError="true" />
    <Message Condition="'@(_DeployedDll->Count())' == '2'" Importance="high"
             Text="SMWCounters: deployed @(_DeployedDll->'%(Filename)%(Extension)', ', ') -> $(ComponentsPath)" />
    <Warning Condition="'@(_DeployedDll->Count())' != '2'"
             Text="Deployed only [@(_DeployedDll->'%(Filename)%(Extension)', ', ')] of the SMWCounters.dll+SNES.dll pair: LiveSplit likely has the rest locked. Close LiveSplit and rebuild." />
  </Target>
```

- [ ] **Step 4: Build to verify**

```bash
cd "c:/Users/thedo/git/SMWCounters" && dotnet build src/SMWCounters/SMWCounters.csproj
```

Expected: build succeeds; output folder (under `artifacts/bin/SMWCounters/`) contains both `SMWCounters.dll` and `SNES.dll`. Verify:

```bash
find "c:/Users/thedo/git/SMWCounters/artifacts" -name "SNES.dll" | head -3
```

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/SMWCounters.csproj && git commit -m "feat: reference pinned SNES.dll, deploy as a pair, bump to 0.3.0"
```

---

### Task 3: StatusDot pure color mapper (TDD)

**Files:**
- Create: `src/SMWCounters/Snes/StatusDot.cs`
- Test: `test/SMWCounters.Tests/StatusDotTests.cs`

**Interfaces:**
- Produces: `internal static class StatusDot` in `LiveSplit.SmwCounters.Snes` with
  `public static Color ColorFor(string stateName, bool isCoolingDown, string witnessVerdict, long witnessBase, long wramBase)`
  and `public static readonly Color` fields `Green, PaleGreen, Yellow, Blue, Gray, DimGray, Orange, Red`. Consumed by Task 5 (`SnesConnection.DotColor`).
- Takes primitives, NOT `SNES.EmuStatus` — `EmuStatus` setters are internal to SNES.dll, so tests could never construct one.

- [ ] **Step 1: Write the failing tests**

Create `test/SMWCounters.Tests/StatusDotTests.cs`:

```csharp
using LiveSplit.SmwCounters.Snes;

using Xunit;

namespace SMWCounters.Tests;

// Pins the status-pixel mapping from the SNES.dll consumer contract
// (snes_offsets/docs/status-first-consumption.md, "Status-pixel mapping").
public class StatusDotTests
{
    [Fact]
    public void Resolved_WithRealVerdictOnCommittedBase_IsGreen()
        => Assert.Equal(StatusDot.Green,
            StatusDot.ColorFor("Resolved", false, "Real", 0x1000, 0x1000));

    [Fact]
    public void Resolved_WithVerdictStampedOnRivalBase_IsPaleGreen()
        => Assert.Equal(StatusDot.PaleGreen,
            StatusDot.ColorFor("Resolved", false, "Real", 0x2000, 0x1000));

    [Fact]
    public void Resolved_WithoutRealVerdict_IsPaleGreen()
        => Assert.Equal(StatusDot.PaleGreen,
            StatusDot.ColorFor("Resolved", false, "Ambiguous", 0x1000, 0x1000));

    [Fact]
    public void Held_FollowsResolvedRules_GreenWhenVouched()
        => Assert.Equal(StatusDot.Green,
            StatusDot.ColorFor("Held", false, "Real", 0x1000, 0x1000));

    [Fact]
    public void Held_FollowsResolvedRules_PaleGreenOtherwise()
        => Assert.Equal(StatusDot.PaleGreen,
            StatusDot.ColorFor("Held", false, "", 0, 0x1000));

    [Fact]
    public void Degraded_IsYellow_EvenWhenCoolingDown()
        => Assert.Equal(StatusDot.Yellow,
            StatusDot.ColorFor("Degraded", true, "Real", 0x1000, 0x1000));

    [Fact]
    public void Discovering_IsBlue()
        => Assert.Equal(StatusDot.Blue,
            StatusDot.ColorFor("Discovering", false, "", 0, 0));

    [Fact]
    public void Searching_IsGray()
        => Assert.Equal(StatusDot.Gray,
            StatusDot.ColorFor("Searching", false, "", 0, 0));

    [Fact]
    public void Searching_WhileCoolingDown_IsOrange()
        => Assert.Equal(StatusDot.Orange,
            StatusDot.ColorFor("Searching", true, "", 0, 0));

    [Fact]
    public void NoContent_IsDimGray()
        => Assert.Equal(StatusDot.DimGray,
            StatusDot.ColorFor("NoContent", false, "", 0, 0));

    [Fact]
    public void NoContent_WhileCoolingDown_IsOrange()
        => Assert.Equal(StatusDot.Orange,
            StatusDot.ColorFor("NoContent", true, "", 0, 0));

    [Fact]
    public void Detached_IsRed_EvenWhenCoolingDown()
        => Assert.Equal(StatusDot.Red,
            StatusDot.ColorFor("Detached", true, "", 0, 0));

    [Fact]
    public void UnknownFutureState_FallsBackToGray()
        => Assert.Equal(StatusDot.Gray,
            StatusDot.ColorFor("SomeNewState", false, "", 0, 0));
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd "c:/Users/thedo/git/SMWCounters" && dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter StatusDotTests
```

Expected: compile FAILURE — `StatusDot` does not exist.

- [ ] **Step 3: Implement StatusDot**

Create `src/SMWCounters/Snes/StatusDot.cs`:

```csharp
using System.Drawing;

namespace LiveSplit.SmwCounters.Snes;

// Pure EmuStatus -> status-dot color mapping, per the SNES.dll consumer
// contract's "Status-pixel mapping (SMWCounters)" section
// (snes_offsets/docs/status-first-consumption.md). Takes primitives rather
// than SNES.EmuStatus so tests can drive it (EmuStatus setters are internal
// to SNES.dll).
internal static class StatusDot
{
    public static readonly Color Green     = Color.FromArgb(0x2E, 0xCC, 0x40); // resolved, witness-vouched
    public static readonly Color PaleGreen = Color.FromArgb(0x94, 0xD8, 0x9C); // resolved, unvouched
    public static readonly Color Yellow    = Color.FromArgb(0xFF, 0xDC, 0x00); // degraded (rivals live)
    public static readonly Color Blue      = Color.FromArgb(0x39, 0x8F, 0xE5); // discovering (scan running)
    public static readonly Color Gray      = Color.FromArgb(0x9A, 0x9A, 0x9A); // searching
    public static readonly Color DimGray   = Color.FromArgb(0x5A, 0x5A, 0x5A); // attached, no content
    public static readonly Color Orange    = Color.FromArgb(0xFF, 0x85, 0x1B); // retry cooldown armed
    public static readonly Color Red       = Color.FromArgb(0xE5, 0x3E, 0x3E); // detached

    // witnessVerdict/witnessBase describe the LAST candidate the identity
    // witness classified, which is only attributable to the committed base
    // when witnessBase == wramBase (an in-flight discovery can stamp a rival).
    // Cooldown-orange overrides only the unresolved states: on the
    // resolved family (Resolved/Held/Degraded) reads stay valid, so the dot
    // keeps reporting that.
    public static Color ColorFor(string stateName, bool isCoolingDown,
                                 string witnessVerdict, long witnessBase, long wramBase)
    {
        switch (stateName)
        {
            case "Resolved":
            case "Held":
                return witnessVerdict == "Real" && witnessBase == wramBase ? Green : PaleGreen;
            case "Degraded":
                return Yellow;
            case "Detached":
                return Red;
            case "Discovering":
                return isCoolingDown ? Orange : Blue;
            case "Searching":
                return isCoolingDown ? Orange : Gray;
            case "NoContent":
                return isCoolingDown ? Orange : DimGray;
            default:
                return Gray; // unknown future state: render as idle searching
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter StatusDotTests
```

Expected: 13 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Snes/StatusDot.cs test/SMWCounters.Tests/StatusDotTests.cs && git commit -m "feat: StatusDot color mapper per the consumer-contract status-pixel mapping"
```

---

### Task 4: StatusChangeFilter — log-on-change idiom (TDD)

**Files:**
- Create: `src/SMWCounters/Snes/StatusChangeFilter.cs`
- Test: `test/SMWCounters.Tests/StatusChangeFilterTests.cs`

**Interfaces:**
- Produces: `internal sealed class StatusChangeFilter` in `LiveSplit.SmwCounters.Snes` with
  `public string OnStatus(string stateName, int generation, long wramBase, bool isCoolingDown, string lastError, string methodName, string rebindReasonName, long scanTotalMs)` (returns a formatted line on change, `null` when the change-key is unchanged) and `public void Reset()`.
  Consumed by Task 6 (`DebugLogger.LogStatus`).
- The change-key is exactly the contract's tuple: `(StateName, Generation, WramBase, IsCoolingDown, LastError)`. Method/rebind/scanMs are payload, not key.

- [ ] **Step 1: Write the failing tests**

Create `test/SMWCounters.Tests/StatusChangeFilterTests.cs`:

```csharp
using LiveSplit.SmwCounters.Snes;

using Xunit;

namespace SMWCounters.Tests;

// Pins the consumer contract's log-on-change idiom: one line per change of
// (StateName, Generation, WramBase, IsCoolingDown, LastError); everything
// else is payload and must not retrigger logging.
public class StatusChangeFilterTests
{
    [Fact]
    public void FirstStatus_ProducesALine()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        Assert.NotNull(line);
        Assert.Contains("Searching", line);
    }

    [Fact]
    public void UnchangedKey_ProducesNull()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        Assert.Null(f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0));
    }

    [Fact]
    public void PayloadOnlyChange_DoesNotRetrigger()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        // Method/rebind/scanMs are payload, not part of the change-key.
        Assert.Null(f.OnStatus("Searching", 0, 0, false, "", "Structural", "Fresh", 1234));
    }

    [Fact]
    public void GenerationBump_Retriggers()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Resolved", 1, 0x7E0000, false, "", "Structural", "Fresh", 900);
        string line = f.OnStatus("Resolved", 2, 0x7E0000, false, "", "Structural", "RivalEviction", 900);
        Assert.NotNull(line);
        Assert.Contains("gen=2", line);
        Assert.Contains("rebind=RivalEviction", line);
    }

    [Fact]
    public void ResolvedLine_CarriesBaseMethodAndScanMs()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Resolved", 1, 0x7E12A000, false, "", "Structural", "Fresh", 8300);
        Assert.Contains("state=Resolved", line);
        Assert.Contains("base=0x7E12A000", line);
        Assert.Contains("method=Structural", line);
        Assert.Contains("scanMs=8300", line);
    }

    [Fact]
    public void UnresolvedLine_OmitsScanMs()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Searching", 0, 0, false, "", "None", "None", 4200);
        Assert.DoesNotContain("scanMs", line);
    }

    [Fact]
    public void ErrorAndCooldown_AppearInTheLine()
    {
        var f = new StatusChangeFilter();
        string line = f.OnStatus("Searching", 0, 0, true,
            "no committable WRAM candidate in the verified set", "None", "None", 0);
        Assert.Contains("cooldown", line);
        Assert.Contains("no committable WRAM candidate", line);
    }

    [Fact]
    public void Reset_MakesTheNextStatusLogAgain()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0);
        f.Reset();
        Assert.NotNull(f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0));
    }

    [Fact]
    public void NullLastError_IsTreatedAsEmpty()
    {
        var f = new StatusChangeFilter();
        f.OnStatus("Searching", 0, 0, false, null, "None", "None", 0);
        Assert.Null(f.OnStatus("Searching", 0, 0, false, "", "None", "None", 0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter StatusChangeFilterTests
```

Expected: compile FAILURE — `StatusChangeFilter` does not exist.

- [ ] **Step 3: Implement StatusChangeFilter**

Create `src/SMWCounters/Snes/StatusChangeFilter.cs`:

```csharp
namespace LiveSplit.SmwCounters.Snes;

// Log-on-change filter for SNES.dll status transitions: the consumer
// contract's idiom is to log only on change of (StateName, Generation,
// WramBase, IsCoolingDown, LastError), which kills the rotating-message
// noise of per-tick logging. Pure primitives in, formatted line (or null)
// out, so tests can drive it without SNES.dll types.
internal sealed class StatusChangeFilter
{
    private string lastKey;

    public string OnStatus(string stateName, int generation, long wramBase,
                           bool isCoolingDown, string lastError,
                           string methodName, string rebindReasonName, long scanTotalMs)
    {
        lastError = lastError ?? "";
        string key = $"{stateName}|{generation}|{wramBase}|{isCoolingDown}|{lastError}";
        if (key == lastKey) { return null; }
        lastKey = key;

        string line = $"SNS state={stateName} gen={generation} base=0x{wramBase:X}"
            + $" method={methodName} rebind={rebindReasonName}"
            + (isCoolingDown ? " cooldown" : "")
            + (lastError.Length == 0 ? "" : $" err=\"{lastError}\"");

        bool resolvedFamily = stateName == "Resolved" || stateName == "Degraded" || stateName == "Held";
        if (resolvedFamily && scanTotalMs > 0)
        {
            line += $" scanMs={scanTotalMs}";
        }
        return line;
    }

    public void Reset() => lastKey = null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj --filter StatusChangeFilterTests
```

Expected: 9 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SMWCounters/Snes/StatusChangeFilter.cs test/SMWCounters.Tests/StatusChangeFilterTests.cs && git commit -m "feat: StatusChangeFilter — log-on-change idiom for status transitions"
```

---

### Task 5: SnesConnection bridge

**Files:**
- Create: `src/SMWCounters/Snes/SnesConnection.cs`

**Interfaces:**
- Consumes: `SNES.Emu` (`Attach(Process)`, `Ready()`, `GetOffset()`, `Read1(int)`, `Status()`, `Generation`), `StatusDot.ColorFor` (Task 3), existing `ISnesMemory`.
- Produces (consumed by Task 7's component rewire and Task 6's logger call site):
  - `public void Tick()` — drive once per poll tick, always.
  - `public SNES.EmuStatus Status { get; }` — latest snapshot, never null.
  - `public System.Drawing.Color DotColor { get; }`
  - `public string Describe()` — human status-line fragment.
  - `ISnesMemory`: `IsAttached`, `ReadWramByte(int, out byte)`.

No unit tests: every member is process/Emu integration, exercised by the Task 9 live gate. The pure logic it delegates to (color mapping, log filtering) is tested in Tasks 3–4.

- [ ] **Step 1: Implement SnesConnection**

Create `src/SMWCounters/Snes/SnesConnection.cs`:

```csharp
using System.Diagnostics;
using System.Drawing;

using SNES;

namespace LiveSplit.SmwCounters.Snes;

// Bridges SNES.dll's structural WRAM discovery to the counters' ISnesMemory
// seam, driving the status-first consumer idiom
// (snes_offsets/docs/status-first-consumption.md) once per poll tick:
//   Ready() throw => not ready; GetOffset() retry while not ready (silent —
//   discovery runs on SNES.dll's own background task); Status() for telemetry.
// Exceptions are control flow, not telemetry: messages are never parsed.
internal sealed class SnesConnection : ISnesMemory
{
    // Mirrors ../kaizosplits/Kaizo.asl's state() declarations, in order.
    // The kaizosplits autosplitter is the source of truth for this list so
    // both components always attach to the same emulator process — do not
    // reorder or extend without changing Kaizo.asl first.
    private static readonly string[] ProcessNames =
    {
        "snes9x", "snes9x-x64", "bsnes", "retroarch", "higan",
        "snes9x-rr", "mesen", "emuhawk", "ares", "mednafen",
    };

    // Process enumeration is comparatively expensive; at the 15 ms poll rate
    // an unthrottled scan would run ~66x/sec while no emulator is open.
    private const int AcquireIntervalMs = 1000;

    private readonly Emu emu = new();
    private readonly Stopwatch acquireClock = Stopwatch.StartNew();
    private Process process;
    private bool ready;
    private int lastGeneration = -1;
    private long lastAcquireMs = -AcquireIntervalMs;

    public SnesConnection()
    {
        Status = emu.Status(); // Detached snapshot; Diag is never null
    }

    public EmuStatus Status { get; private set; }

    public Color DotColor => StatusDot.ColorFor(
        Status.StateName, Status.IsCoolingDown,
        Status.WitnessVerdict, Status.WitnessBase, Status.WramBase);

    public bool IsAttached => ready && process != null && !process.HasExited;

    // Drive attach/discovery one step. Called every poll tick regardless of
    // timer phase (always-on discovery: the dot should be green before a run
    // starts). Non-blocking: discovery runs on SNES.dll's background task and
    // GetOffset() throws while it is in flight.
    public void Tick()
    {
        if (process != null && process.HasExited)
        {
            process = null;
            ready = false;
        }

        if (process == null && acquireClock.ElapsedMilliseconds - lastAcquireMs >= AcquireIntervalMs)
        {
            lastAcquireMs = acquireClock.ElapsedMilliseconds;
            process = FindEmulatorProcess();
            if (process != null)
            {
                emu.Attach(process);
                ready = false;
            }
        }

        if (process != null)
        {
            // A rival eviction rebinds silently (no Ready() throw) but bumps
            // Generation; every watcher bound to the old base must re-baseline,
            // which dropping `ready` achieves (IsAttached goes false for a
            // tick, so counters flush their PreviousByte state).
            if (ready && emu.Generation != lastGeneration) { ready = false; }

            try { emu.Ready(); } catch { ready = false; }

            if (!ready)
            {
                try
                {
                    emu.GetOffset();
                    ready = true;
                    lastGeneration = emu.Generation;
                }
                catch { /* in flight, cooling down, or declined — Status() carries the news */ }
            }
        }

        Status = emu.Status();
    }

    public bool ReadWramByte(int snesOffset, out byte value)
    {
        value = 0;
        if (!IsAttached) { return false; }
        try
        {
            value = emu.Read1(snesOffset);
            return true;
        }
        catch
        {
            return false; // lost mid-tick; next Tick() re-enters the idiom
        }
    }

    // Human status-line fragment for the settings panel.
    public string Describe()
    {
        EmuStatus s = Status;
        string proc = process != null ? process.ProcessName : "?";
        switch (s.StateName)
        {
            case "Detached":
                return "No emulator found";
            case "NoContent":
                return $"{proc} · no game detected{CooldownSuffix(s)}";
            case "Searching":
                return $"{proc} · searching for game{CooldownSuffix(s)}{ErrorSuffix(s)}";
            case "Discovering":
                return $"{proc} · discovering WRAM…";
            case "Resolved":
            case "Held":
            case "Degraded":
                return $"{proc} · WRAM @ 0x{s.WramBase:X} ({s.MethodName})";
            default:
                return $"{proc} · {s.StateName}";
        }
    }

    private static string CooldownSuffix(EmuStatus s)
        => s.IsCoolingDown ? " (retry pending)" : "";

    private static string ErrorSuffix(EmuStatus s)
        => string.IsNullOrEmpty(s.LastError) ? "" : $" — {s.LastError}";

    private static Process FindEmulatorProcess()
    {
        foreach (string name in ProcessNames)
        {
            Process[] found = Process.GetProcessesByName(name);
            Process alive = null;
            foreach (Process p in found)
            {
                if (alive == null && !p.HasExited) { alive = p; }
                else { p.Dispose(); }
            }
            if (alive != null) { return alive; }
        }
        return null;
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
dotnet build src/SMWCounters/SMWCounters.csproj
```

Expected: success (class is not yet referenced; that's Task 7).

- [ ] **Step 3: Commit**

```bash
git add src/SMWCounters/Snes/SnesConnection.cs && git commit -m "feat: SnesConnection — status-first SNES.dll bridge behind ISnesMemory"
```

---

### Task 6: DebugLogger status-transition logging

**Files:**
- Modify: `src/SMWCounters/Diagnostics/DebugLogger.cs`

**Interfaces:**
- Consumes: `StatusChangeFilter` (Task 4), `SNES.EmuStatus`.
- Produces: `public void LogStatus(SNES.EmuStatus status)` on `DebugLogger` — consumed by Task 7's `Poll()`.

No new unit tests: the change-detection and formatting logic is `StatusChangeFilter`, already tested in Task 4; `LogStatus` is a thin adapter plus file I/O (which this class keeps best-effort/swallowed by design).

- [ ] **Step 1: Add the filter field and LogStatus method**

In `DebugLogger.cs`, add a field next to the existing `lastValue` field:

```csharp
    private readonly StatusChangeFilter statusFilter = new();
```

Add this method after `Poll(...)`:

```csharp
    // Log SNES.dll status transitions ("SNS ..." lines): one line per change
    // of (StateName, Generation, WramBase, IsCoolingDown, LastError), plus
    // method/rebind provenance and scan latency on resolves. Called every
    // tick while debug logging is enabled — including when the timer is not
    // running, since always-on discovery transitions happen pre-run too.
    public void LogStatus(SNES.EmuStatus status)
    {
        string line = statusFilter.OnStatus(
            status.StateName, status.Generation, status.WramBase,
            status.IsCoolingDown, status.LastError,
            status.MethodName, status.RebindReasonName,
            status.Diag != null ? status.Diag.ScanTotalMs : 0);
        if (line != null) { Write(line); }
    }
```

In `Close()`, add `statusFilter.Reset();` after the `Idle();` call, so a re-enabled log starts with a fresh status line. Do NOT reset it in `Idle()` — status continuity must span pauses, or every pause/resume would relog an unchanged state.

- [ ] **Step 2: Build and run the full test suite**

```bash
dotnet build src/SMWCounters/SMWCounters.csproj && dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj
```

Expected: build succeeds, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/SMWCounters/Diagnostics/DebugLogger.cs && git commit -m "feat: debug log gains SNS status-transition lines (log-on-change)"
```

---

### Task 7: Component rewire — always-on discovery, status dot, delete the offset tables

**Files:**
- Modify: `src/SMWCounters/UI/Components/SmwCountersComponent.cs`
- Delete: `src/SMWCounters/Snes/SnesEmu.cs`, `src/SMWCounters/Snes/Offsets.cs`

**Interfaces:**
- Consumes: `SnesConnection` (Task 5: `Tick()`, `Status`, `DotColor`, `Describe()`, `ISnesMemory`), `DebugLogger.LogStatus` (Task 6).
- Produces: the shipped behavior. Nothing downstream consumes component internals.

- [ ] **Step 1: Swap the field**

In `SmwCountersComponent.cs`, replace:

```csharp
    private readonly SnesEmu emu = new();
```

with:

```csharp
    private readonly SnesConnection connection = new();
```

- [ ] **Step 2: Rewrite Poll()**

Replace the entire `Poll()` method with:

```csharp
    private void Poll()
    {
        // Always-on discovery: the connection ticks every poll regardless of
        // timer phase, so the status dot is already green when a run starts
        // (structural discovery takes seconds; gating it on the timer would
        // lose the first seconds of counting).
        connection.Tick();
        if (Settings.DebugLog) { debugLog.LogStatus(connection.Status); }

        // Counters still only count during a live run. NotRunning covers
        // title screen / file select / overworld-before-start (where SMW
        // demos and casual play would otherwise pollute the counter); Ended
        // covers post-run idle. Paused counts as active so a pause/resume
        // preserves edge continuity.
        bool timerActive = state.CurrentPhase == TimerPhase.Running
            || state.CurrentPhase == TimerPhase.Paused;

        if (!timerActive)
        {
            // Flush each counter's previous-byte state so that resuming after
            // a gap doesn't bridge a stale sample to a fresh one and produce
            // a spurious edge.
            foreach (ISmwCounter c in counters)
            {
                if (Settings.IsEnabled(c.Id)) { c.Poll(inert); }
            }
            debugLog.Idle();
            Settings.SetStatus("Paused · timer not running · " + connection.Describe());
            return;
        }

        // Poll counters against the live connection even when it is not
        // (yet) attached: each counter's !IsAttached branch flushes its edge
        // state, so a mid-run detach can't bridge stale samples on reattach.
        foreach (ISmwCounter c in counters)
        {
            if (c is PowerupCounter pc) { pc.Banked = Settings.IsBankOnSave(c.Id); }
            if (Settings.IsEnabled(c.Id)) { c.Poll(connection); }
        }

        if (!connection.IsAttached)
        {
            debugLog.Idle();
            Settings.SetStatus(connection.Describe());
            return;
        }

        if (Settings.DebugLog)
        {
            debugLog.Poll(connection, counters, id => Settings.IsEnabled(id),
                          state.CurrentPhase.ToString(), connection.Describe());
            Settings.SetStatus("Counting · " + connection.Describe() + " · debug log → " + debugLog.LogPath);
        }
        else
        {
            debugLog.Close();
            Settings.SetStatus("Counting · " + connection.Describe());
        }
    }
```

- [ ] **Step 3: Add the dot to the invalidation cache**

In `Update(...)`, after `cache.Restart();`, add:

```csharp
        cache["dot"] = connection.DotColor.ToArgb();
```

- [ ] **Step 4: Draw the dot leading the row**

In `DrawGeneral(...)`:

a. After the `int iconHeight = ...` line, add:

```csharp
        // Status dot: a small connection-health LED leading the row. Size
        // tracks the row so layout scaling keeps it visible but subtle.
        float dotDiameter = Math.Max(4f, 0.25f * Settings.RowHeight);
        const float dotGap = 6f;
```

b. The dot participates in width/centering. Replace:

```csharp
        HorizontalWidth = totalWidth + 15;
```

with:

```csharp
        totalWidth += dotDiameter + dotGap;
        HorizontalWidth = totalWidth + 15;
```

(The measure loop above it is unchanged — the dot contribution is added once, after the per-cell loop.)

c. Immediately after the `float x = Settings.Alignment switch { ... };` statement, draw the dot and advance `x`:

```csharp
        SmoothingMode prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var dotBrush = new SolidBrush(connection.DotColor))
        {
            g.FillEllipse(dotBrush, x, (height - dotDiameter) / 2f, dotDiameter, dotDiameter);
        }
        g.SmoothingMode = prevSmoothing;
        x += dotDiameter + dotGap;
```

(`SmoothingMode` lives in the already-imported `System.Drawing.Drawing2D`.)

- [ ] **Step 5: Delete the offset-table backend**

```bash
git rm src/SMWCounters/Snes/SnesEmu.cs src/SMWCounters/Snes/Offsets.cs
```

- [ ] **Step 6: Build and run the full test suite**

```bash
dotnet build src/SMWCounters/SMWCounters.csproj && dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj
```

Expected: build succeeds (nothing references `SnesEmu`/`Offsets` anymore; if it fails, a leftover reference needs the same `connection` treatment), all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A src/SMWCounters && git commit -m "feat: always-on SNES.dll discovery + status dot; drop offset tables"
```

---

### Task 8: Release workflow, README, CREDITS

**Files:**
- Modify: `.github/workflows/release.yml`, `README.md`, `CREDITS.md`

**Interfaces:**
- Consumes: build output containing `SNES.dll` (Task 2).
- Produces: release zip + file list shipping both DLLs; user-facing docs describing zero-config discovery and the dot.

- [ ] **Step 1: Stage SNES.dll in release.yml**

In the `Stage release assets` step, after the `Copy-Item $dll.FullName (Join-Path $out "SMWCounters.dll")` line, add:

```powershell
          $snes = Get-ChildItem -Recurse -Filter SNES.dll artifacts | Select-Object -First 1
          if (-not $snes) { throw "SNES.dll not found in build output" }
          Copy-Item $snes.FullName   (Join-Path $out "SNES.dll")
```

and after the `"dll=release-staging/SMWCounters.dll"` output line, add:

```powershell
          "snesdll=release-staging/SNES.dll" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
```

- [ ] **Step 2: Ship it in the release and artifact file lists**

In the `Publish GitHub Release` step, replace the install body and files list:

```yaml
          body: |
            ## Install
            1. Download `SMWCounters.dll` **and** `SNES.dll` below (or the zip, which contains both).
            2. Copy **both** into your `LiveSplit/Components/` folder.
            3. In LiveSplit: right-click → Edit Layout → **+** → **Other → SMW Counters**.

            Works with any SNES emulator build (RetroArch, snes9x, bsnes, higan,
            Mesen, BizHawk, ares, Mednafen) running Super Mario World — WRAM is
            found automatically, no per-version configuration. The LiveSplit
            timer must be running for counts to increment; the status dot shows
            connection health at all times.
          files: |
            ${{ steps.stage.outputs.dll }}
            ${{ steps.stage.outputs.snesdll }}
            ${{ steps.stage.outputs.zip }}
```

In the `Upload build artifacts (manual runs)` step, add the same new path to `path:`:

```yaml
          path: |
            ${{ steps.stage.outputs.dll }}
            ${{ steps.stage.outputs.snesdll }}
            ${{ steps.stage.outputs.zip }}
```

- [ ] **Step 3: Update README.md**

Replace the Install steps 1–2 with:

```markdown
1. Download `SMWCounters.dll` **and** `SNES.dll` from the
   [latest release](https://github.com/amcknight/SMWCounters/releases/latest)
   (the release zip contains both).
2. Copy **both DLLs** into the `Components` folder inside your LiveSplit
   install (e.g. `LiveSplit/Components/`).
```

Replace the Requirements emulator bullet with:

```markdown
- A running SNES emulator with *Super Mario World* loaded: RetroArch, snes9x
  (any variant), bsnes, higan, Mesen, BizHawk, ares, or Mednafen — **any
  version**. WRAM is discovered structurally (via the
  [snes_offsets](https://github.com/amcknight/snes_offsets) project's
  `SNES.dll`), so there are no per-build offset tables to go stale and no
  configuration.
- A colored **status dot** leads the counter row: green = connected to the
  game (pale green = connected, identity unvouched), blue = discovering,
  gray = searching (dim = no game running), orange = retrying shortly,
  yellow = connected with rival candidates, red = no emulator found.
```

Update the Credits line at the bottom: replace "Emulator/offset research is ported from [kaizosplits](...)" with:

```markdown
WRAM discovery is powered by
[snes_offsets](https://github.com/amcknight/snes_offsets) (`SNES.dll`);
earlier releases used offset tables ported from
[kaizosplits](https://github.com/amcknight/kaizosplits).
```

- [ ] **Step 4: Update CREDITS.md**

Replace the "Offset research" section with:

```markdown
## WRAM discovery

`lib/SNES.dll` (pinned v1.6.0) comes from the author's
[snes_offsets](https://github.com/amcknight/snes_offsets) project: structural
SNES WRAM discovery for LiveSplit consumers — no offset tables, reads only.

Earlier releases used emulator-detection and WRAM offset tables ported from
[kaizosplits](https://github.com/amcknight/kaizosplits) (the author's own
earlier LiveSplit SMW project); credit for that original offset research
belongs to that project and its upstream sources.
```

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml README.md CREDITS.md && git commit -m "docs+ci: ship SMWCounters.dll+SNES.dll as a pair; zero-config install docs"
```

---

### Task 9: Verification + live gate

**Files:** none (verification only).

- [ ] **Step 1: Full clean build + tests**

```bash
cd "c:/Users/thedo/git/SMWCounters" && dotnet build SMWCounters.sln && dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj
```

Expected: build success; all tests pass (existing counter suites + 13 StatusDot + 9 StatusChangeFilter).

- [ ] **Step 2: Verify the deploy pair staging**

Confirm the build output contains both DLLs and that (when `ComponentsPath` is configured in `SMWCounters.local.props` and LiveSplit is closed) the post-build copy reported deploying the pair — the build log line `SMWCounters: deployed SMWCounters.dll, SNES.dll -> ...`. If LiveSplit was open, close it and rebuild until the pair deploys (stale-DLL trap).

- [ ] **Step 3: Live gate (requires Andrew — blocks merge, not the branch)**

Run with the debug log enabled; evidence is the `SNS` lines in
`%LOCALAPPDATA%/SMWCounters/counters-debug.log`:

1. LiveSplit closed → build → confirm pair deployed with fresh timestamps.
2. Vanilla SMW in snes9x: dot gray→blue→green within seconds of the game
   loading; counters count correctly during a run.
3. Same in RetroArch (snes9x core).
4. Pause / sit in a menu → dot stays green-family; no false counter edges.
5. Close the emulator mid-session → dot red; reopen + reload the ROM → green
   again; counters resume cleanly (no spurious increments on reattach).
6. Log shows the status-transition trail: one `SNS` line per state change and
   a resolve line with `method=` and `scanMs=`.

- [ ] **Step 4: Merge (only after the live gate passes)**

Use the superpowers:finishing-a-development-branch skill: merge
`feat/snes-dll-discovery` → `master`, delete the branch.

---

## Self-review notes

- **Spec coverage:** decisions 1–6 map to Tasks 5/7 (replacement, always-on, process list), 7 (dot placement), 1–2 (pinned lib DLL + dev mode), 3 (mapper). Throttle → Task 5 (`AcquireIntervalMs`). Logging idiom → Tasks 4/6. Packaging/release → Tasks 1/2/8. Edge cases → SnesConnection/Poll comments + live gate. Testing/merge gate → Task 9.
- **Placeholder scan:** all code blocks are complete implementations; no TBDs.
- **Type consistency:** `StatusDot.ColorFor(string, bool, string, long, long)` used identically in Tasks 3 and 5; `StatusChangeFilter.OnStatus(string, int, long, bool, string, string, string, long)` identical in Tasks 4 and 6; `SnesConnection` members consumed in Task 7 match Task 5's definitions (`Tick`, `Status`, `DotColor`, `Describe`, `IsAttached`, `ReadWramByte`).
