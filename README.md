# LiveSplit SMW Counters

A [LiveSplit](https://livesplit.org/) layout component that shows live *Super
Mario World* counters — **deaths**, **level exits**, **jumps**, and **3-up
moons** — by reading SNES WRAM from your running emulator.

<!-- TODO: add a screenshot of the component in a LiveSplit layout. -->

## Install

1. Download `SMWCounters.dll` from the
   [latest release](https://github.com/amcknight/SMWCounters/releases/latest).
2. Copy it into the `Components` folder inside your LiveSplit install
   (e.g. `LiveSplit/Components/`).
3. Start LiveSplit → right-click → **Edit Layout…** → **+** → **Other →
   SMW Counters**.
4. Save the layout.

## Requirements

- **LiveSplit** 1.8.37 or newer.
- A running, supported SNES emulator with *Super Mario World* loaded:
  RetroArch, snes9x (including `snes9x-x64` and `snes9x-rr`), bsnes, higan, or
  BizHawk.
- Counting only happens while the LiveSplit timer is running or paused, so the
  title screen / file select / demos don't pollute the counts.

## Configuration

Open the component's settings (Edit Layout → double-click **SMW Counters**):

- **Enable/disable** each counter independently (deaths and exits are on by
  default).
- **3-up moon dedupe mode** — count **All** moons, or de-duplicate **Per level**
  or **Per room**.
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

Code is MIT-licensed (see [LICENSE](LICENSE)). Emulator/offset research is
ported from [kaizosplits](https://github.com/amcknight/kaizosplits). *Super
Mario World* sprites are Nintendo's, used as fan iconography — see
[CREDITS.md](CREDITS.md).
