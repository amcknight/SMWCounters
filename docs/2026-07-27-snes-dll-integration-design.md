# SNES.dll integration — zero-config WRAM discovery + status dot

**Date:** 2026-07-27 · **Target version:** 0.3.0 · **Branch:** `feat/snes-dll-discovery`

## Goal

Replace the ported kaizosplits offset tables (`Snes/SnesEmu.cs` + `Snes/Offsets.cs`)
with `SNES.dll` v1.6.0 from the sibling `snes_offsets` repo: structural WRAM
discovery that works on any emulator build with zero user configuration. Add a
status dot to the component row so users can see connection health at a glance.
This is the step that makes SMWCounters usable by a wide audience — no more
"unknown build (module size N)" dead ends.

## Decisions (settled in brainstorming)

1. **Full replacement.** The offset tables are deleted, not kept as a fallback.
   Matches snes_offsets' own v1.6.0 decision (structural discovery is the sole
   authority) and kaizosplits' consumption.
2. **Always-on discovery.** Attach/discovery runs every poll tick regardless of
   timer phase, so the dot is green before a run starts. Counters still only
   count while the timer is Running/Paused (unchanged).
3. **Process selection mirrors the autosplitter exactly.** Kaizo.asl's ordered
   10-name list — `snes9x, snes9x-x64, bsnes, retroarch, higan, snes9x-rr,
   mesen, emuhawk, ares, mednafen` — first running match wins, no rotation.
   SNES.dll does not pick processes; the consumer does. Mirroring guarantees
   SMWCounters and the kaizosplits autosplitter attach to the same process. A
   comment pins Kaizo.asl as the source of truth for the list.
4. **Status dot leads the row (far left)**, like a status LED. Diameter
   `max(4px, 0.25 × RowHeight)`, vertically centered, small gap before the
   first counter cell.
5. **SNES.dll is a pinned binary committed to `lib/`** (v1.6.0 Release, built
   from snes_offsets `d5777e6`). No cross-repo build coupling; upgrades are
   explicit, reviewable bumps. Optional dev mode builds against sibling source.
6. **Structure: one bridge class + a pure color mapper** (approach A). No
   speculative seams; counters' existing `ISnesMemory` seam is untouched.

## Architecture

### New: `Snes/SnesConnection.cs` (internal sealed, implements `ISnesMemory`)

Owns a concrete `SNES.Emu` and a `Process`. Replaces `SnesEmu.cs` and
`Offsets.cs` (both deleted).

`Tick()` — called from `SmwCountersComponent.Poll()` every 15 ms tick,
unconditionally. The status-first consumer idiom from
`snes_offsets/docs/status-first-consumption.md`:

1. **Process (re)acquire.** If no live process (`null` or `HasExited`): scan
   the ordered name list, take the first running match, `emu.Attach(process)`,
   `ready = false`. No match → remain detached.
2. **Rebind watch.** If `ready` and `Status().Generation` != last seen
   generation → `ready = false` (covers silent rival-eviction rebinds).
3. `try { emu.Ready(); } catch { ready = false; }` — the throw IS the
   not-ready signal; the message is never parsed.
4. If not ready: `try { emu.GetOffset(); ready = true; } catch { /* silent */ }`
   — non-blocking; SNES.dll runs discovery on its own background task and
   throws from `GetOffset()` while in flight.
5. Snapshot `emu.Status()` (never throws, never blocks) for the dot, the
   status line, and logging. Record `Generation`.

Members:

- `ISnesMemory.IsAttached` → process alive && `ready`.
- `ISnesMemory.ReadWramByte(offset, out value)` → `emu.Read1(offset)` wrapped
  in try/catch → `false` on throw. (Reads are console-space: `$7E0000` →
  offset 0, same address space the counters already use.)
- `Status` → last `EmuStatus` snapshot (for dot + status line + logger).
- `Describe()` → human status line: state name, process name, method/base or
  `LastError` as appropriate.

**Counter edge-state flushing comes free:** any rebind or not-ready flips
`IsAttached` to false; the counters' existing on-detach branch flushes their
`PreviousByte` state. No new counter API, no changes to `ISnesMemory`.

### New: `Snes/StatusDot.cs` (pure static)

`Color ColorFor(string stateName, bool isCoolingDown, string witnessVerdict,
long witnessBase, long wramBase)` implementing the mapping pre-specced in
`snes_offsets/docs/status-first-consumption.md` §"Status-pixel mapping
(SMWCounters)":

| Condition | Color |
| --- | --- |
| `Resolved` + witness `Real` (and `WitnessBase == WramBase`) | green |
| `Resolved` otherwise | pale green |
| `Held` | same as Resolved rules (reads stay valid on a paused game) |
| `Degraded` | yellow |
| `Discovering` | blue |
| `Searching` | gray |
| `NoContent` | dim gray |
| `IsCoolingDown` (overrides non-resolved states only) | orange |
| `Detached` | red |

Pure function → unit-testable in the existing test project with no emulator.

### Changed: `UI/Components/SmwCountersComponent.cs`

- `Poll()`: `connection.Tick()` runs first, every tick (always-on). The
  timer-phase gate then only decides whether counters poll the live connection
  or the inert flush path — the existing structure, minus the old
  `TryAttach()` early-return.
- `DrawGeneral()`: draws the dot leading the row; row width accounts for
  dot + gap. Drawn whenever the component draws, including pre-run.
- `Update()`: dot color joins the `GraphicsCache` key set so state changes
  invalidate/repaint.
- Settings status line (`SetStatus`): always includes connection state, e.g.
  `Counting · Resolved (Structural) · snes9x` or
  `Paused · timer not running · Searching · snes9x`. `LastError` shown when
  present.

### Changed: `Diagnostics/DebugLogger.cs`

When debug logging is on:

- One line per change of `(StateName, Generation, WramBase, IsCoolingDown,
  LastError)` — the contract's log-on-change idiom (kills rotating-message
  noise).
- On each resolve, one line with `Method`, `RebindReason`, and `Diag` scan
  timings (`Scan*`), keeping counters-debug.log the evidence source for
  discovery behavior.

## Build, packaging, release

- **`lib/SNES.dll` committed** (v1.6.0 Release build). `lib/` currently tracks
  only `.gitkeep`; add a gitignore carve-out (`!lib/SNES.dll`) if needed.
  CREDITS.md notes the pinned version and provenance (snes_offsets project).
- **csproj:** `<Reference Include="SNES">` with HintPath into `lib/`,
  **`Private=true`** (unlike LiveSplit.Core's `Private=false`) so SNES.dll
  lands in build output and ships with the component.
- **Deploy pair:** `CopyToLiveSplitComponents` copies `SNES.dll` alongside
  `SMWCounters.dll` (+`Touch`), kaizosplits-style; on lock failure the warning
  names which of the pair didn't deploy (stale-DLL trap visibility).
- **Dev mode (optional):** `SnesSrcPath` in gitignored
  `SMWCounters.local.props` switches to a `ProjectReference` against
  `../snes_offsets/src/SNES/SNES.csproj` (requires sibling LiveSplit checkout,
  which SNES.csproj still references).
- **release.yml:** stage `SNES.dll` into the zip and the release file list;
  release-body install instructions become "copy **both** DLLs into
  `LiveSplit/Components/`". Version → 0.3.0.
- **Runtime resolution:** LiveSplit loads components via `Assembly.LoadFrom`;
  a same-folder dependency resolves via LoadFrom probing. Expected to work
  unmodified; explicitly verified in the live gate.

## Error handling / edge cases

- **Emulator exits:** `HasExited` → detach, dot red, `IsAttached` false
  flushes counter edge state; tick loop keeps scanning. Reopen →
  auto-reattach (SNES.dll fast-reattach path makes this quick).
- **Pause / menus / file-select:** `Held` — reads stay valid on the frozen
  snapshot, no spurious edges, dot stays green-family; resume is instant, no
  Generation bump.
- **ROM swap:** Generation bump / `SmcChanged` → re-baseline via the rebind
  flow; counter values persist (resetting remains the user's call).
- **Mid-discovery / read failure:** `Read1` throws → `ReadWramByte` false →
  counter skips the sample (existing behavior on failed reads).
- **Structural decline** (e.g. custom-GFX hack the witness bank can't vouch
  and the pair path can't bind): honest gray dot + `LastError` evidence in
  the status line; never a wrong-address bind (v1.5.2 Real-only policy).
- **Two emulators open:** first match, same as the autosplitter — consistent
  with it by construction; closing the idle one heals both.

## Testing and merge gate

**Unit (CI-safe, no emulator):**

- `StatusDot.ColorFor` mapping table test (every state × cooldown × verdict
  combination in the table above).
- Log-on-change key test (transition detection, no line when unchanged).
- All existing counter tests pass untouched.

**Build checks:**

- Clean standalone build (no sibling repos) succeeds.
- `workflow_dispatch` CI run; artifact zip contains both DLLs.

**Live gate (blocks merge to master; evidence in counters-debug.log):**

1. LiveSplit closed → build → verify the pair deployed (stale-DLL trap:
   confirm timestamps/build stamp).
2. Vanilla SMW in snes9x: dot gray→blue→green within seconds of content;
   counters count correctly during a run.
3. Same in RetroArch (snes9x core).
4. Pause / sit in a menu → dot stays green-family, no false counter edges.
5. Close emulator mid-session → red; reopen + reload ROM → green again,
   counters resume cleanly.
6. Debug log shows the status-transition trail (state changes, resolve line
   with Method + scan ms).

Merge `feat/snes-dll-discovery` → `master` only after the live gate passes.

## Out of scope

- Process rotation on `NoContent` (would desync from the autosplitter; can
  revisit if kaizosplits adopts it).
- User-configurable process names.
- SNES.dll changes of any kind (consume v1.6.0 as-is; F1 LiveSplit-dependency
  vendoring is snes_offsets' future work).
- Counter behavior changes.
