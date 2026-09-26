# LiveSplit SMW Counters

A [LiveSplit](https://livesplit.org/) layout component that shows live *Super
Mario World* counters — **deaths**, **level exits**, **3-up moons**,
**jumps**, **powerups**, **coins**, and **kills** — by reading SNES WRAM from
your running emulator. Built for challenge runs (low%, low-jump, pacifist,
deathless) on vanilla SMW and ROM hacks.

<!-- TODO: add a screenshot of the component in a LiveSplit layout. -->

## Install

1. Download `SMWCounters.dll` **and** `SNES.dll` from the
   [latest release](https://github.com/amcknight/SMWCounters/releases/latest)
   (the release zip contains both).
2. Copy **both DLLs** into the `Components` folder inside your LiveSplit
   install (e.g. `LiveSplit/Components/`).
3. Start LiveSplit → right-click → **Edit Layout…** → **+** → **Other →
   SMW Counters**.
4. Save the layout.

Upgrading: replace both DLLs. An older `SNES.dll` next to a newer
`SMWCounters.dll` will not load.

## Requirements

- **LiveSplit** 1.8.37 or newer.
- A running SNES emulator with *Super Mario World* loaded: RetroArch, snes9x
  (any variant), bsnes, higan, Mesen, BizHawk, ares, or Mednafen — **any
  version**. WRAM is discovered structurally (via the
  [snes_offsets](https://github.com/amcknight/snes_offsets) project's
  `SNES.dll`), so there are no per-build offset tables to go stale and no
  configuration.
- **SA-1 hacks are not supported yet.** `SNES.dll` declines SA-1 cartridges;
  the component shows the reason in its settings status line.

## The counters

Each counter can be shown or hidden independently. Every counter tallies for
the whole run whether or not it is shown, so switching one on mid-run reveals
what it has been counting rather than starting from zero.

| Counter | Counts | Default |
|---|---|---|
| **Deaths** | Each time Mario dies. | shown |
| **Exits** | Each level exit (goal tape, orb, key, boss). | shown |
| **Moons** | 3-up moons collected. | hidden |
| **Jumps** | Player-initiated jumps. Running off a ledge, enemy bounces, and mid-air presses do not count; jumping off Yoshi or out of water does. | hidden |
| **Powerups** | Mushroom, feather, and flower pickups (the grow animation). | hidden |
| **Coins** | Coins collected in levels, as a lifetime tally across 100-coin wraps. Shop deductions and off-level coin writes are ignored. | hidden |
| **Kills** | Creatures killed: stomps, spin jumps, shell hits, fireballs, Yoshi eats, lava, goal-tape coins. Objects (throw blocks, springboards, P-switches, message boxes, …) are excluded. A **Destruction** mode counts everything destroyed instead, objects included. | hidden |

### Discard on death

Moons, Jumps, Powerups, Coins, and Kills are **banked** counters. Collects
since the last checkpoint or exit show in the layout's gold (best-segment)
color; reaching a midway or finishing the level banks them; dying first
discards them. This is what a low% run means by "it only counts if you keep
it".

Each banked counter has a **Discard on death** checkbox in settings. Both
histories (with and without discards) are always tracked; the checkbox only
picks which one is shown, so flipping it mid-run is safe. It defaults on for
everything except Moons.

### What counts, and when

- Counters only count while the LiveSplit timer is running (or paused, or
  after the final split). Untick **Only count when timer running** for
  challenge runs that never start a timer.
- Deaths and exits count only once you are past the title and file-select
  screens, so the title-screen attract demo never counts. Collects count only
  inside a level.
- Counter values are saved with the layout, so they survive closing LiveSplit.

## Settings

Open the component's settings (Edit Layout → double-click **SMW Counters**):

- **Per counter:** show/hide, the current value (editable), a **Reset**
  button, and the **Discard on death** checkbox. Kills has a **Kills /
  Destruction** selector.
- **Reset hotkey** — a global keyboard or gamepad key that zeroes every
  counter.
- **Reset counters when splits reset** — on by default.
- **Only count when timer running** — on by default; see above.
- **Show connection status pixel** — a tiny square in the component's
  top-left corner. Green or blue = reading the game (green is the normal
  steady state on RetroArch). Purple = emulator paused. Yellow = looking for
  the game. Orange = emulator found, no game loaded. Gray = waiting before
  retrying. Red = no emulator found.
- **Write debug events to counters-debug.log** — the file lives in
  `%LocalAppData%\SMWCounters\`. Off by default; turn it on when reporting a
  miscount so the event trail can be read.
- **Row height**, **Alignment**, and **Reserve digits** for layout fit.
  Reserve digits (default 2) is the minimum number of digits every value
  keeps room for. A value's cell only grows once it outgrows that, so
  counters don't shift as digits change.

If you enable more counters than fit on one row, add a second SMW Counters
component to the layout and split the counters between them. The component
never wraps, so its height stays fixed mid-run.

## Build from source

**Standalone (no LiveSplit source tree):**

```sh
pwsh -File scripts/fetch-livesplit-core.ps1   # fetches lib/LiveSplit.Core.dll
dotnet build src/SMWCounters/SMWCounters.csproj -c Release
dotnet test test/SMWCounters.Tests/SMWCounters.Tests.csproj -c Release
```

The built DLL lands under `artifacts/bin/SMWCounters/`. To have every build
copy it (with `SNES.dll`) straight into your LiveSplit `Components` folder,
set `ComponentsPath` in a gitignored `SMWCounters.local.props` next to the
csproj, or pass `-p:ComponentsPath=...`. Close LiveSplit first; it locks the
DLLs.

**Inside the LiveSplit super-repo:** provide `LsSrcPath` (pointing at the
LiveSplit `src` folder) and the project references `LiveSplit.Core` by source
automatically.

## Credits & license

Code is MIT-licensed (see [LICENSE](LICENSE)). WRAM discovery is powered by
[snes_offsets](https://github.com/amcknight/snes_offsets) (`SNES.dll`);
earlier releases used offset tables ported from
[kaizosplits](https://github.com/amcknight/kaizosplits). *Super
Mario World* sprites are Nintendo's, used as fan iconography — see
[CREDITS.md](CREDITS.md).
