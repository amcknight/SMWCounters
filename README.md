# LiveSplit SMW Counters

A [LiveSplit](https://livesplit.org/) layout component that shows live *Super
Mario World* counters — **deaths**, **level exits**, **jumps**, **3-up
moons**, and **powerups** — by reading SNES WRAM from your running emulator.

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

## Requirements

- **LiveSplit** 1.8.37 or newer.
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

## Configuration

Open the component's settings (Edit Layout → double-click **SMW Counters**):

- **Enable/disable** each counter independently (deaths and exits are on by
  default; jumps, moons, and powerups are off by default).
- **3-up moon dedupe mode** — count **All** moons, or de-duplicate **Per level**
  or **Per room**.
- **Powerups** — a low% helper: counts powerups collected, and turns the
  layout's negative color while you're carrying powerups that haven't been
  "banked" yet by a checkpoint or exit. Die before banking to discard them
  without counting. Off by default.
- **Label overrides** — replace a counter's default sprite icon with your own
  text label.
- **Reset key** — a hotkey (keyboard or gamepad) that zeroes the counters.
- **Reset on splits reset** — clear counters whenever the run resets.
- **Alignment** and **row height** for layout fit.

## Build from source

**Standalone (no LiveSplit source tree):**

```sh
pwsh -File scripts/fetch-livesplit-core.ps1   # fetches lib/LiveSplit.Core.dll
dotnet build src/SMWCounters/SMWCounters.csproj -c Release
```

The built DLL lands under `artifacts/bin/SMWCounters/`.

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
