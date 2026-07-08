# Kill Counter — v1 design

## Purpose

Add a counter that tallies enemy kills, read from the SMW sprite-status table.
This is exploratory: the eventual feature may grow into a multi-concept row
(Kills / Passivist / possibly Destruction) distinguished by radios/checkboxes
under one name, and might aim for relevance to challenge categories. But v1 is
deliberately a bare **observation instrument** — the raw count in real play is
what will decide the eventual name, icon, options, and whether any filtering is
even needed.

## Background: the sprite-status table

Core memory is the 12-byte sprite-status array at `$14C8` (offsets
`$14C8`–`$14D3`, one byte per sprite slot). Byte meanings:

| Value | Description |
|-------|-------------|
| `00` | Empty |
| `01` | Slot taken, not yet initialized |
| `02` | Killed and falling off screen |
| `03` | Killed by smushing (shell-less Koopa, P-switch) |
| `04` | Killed with a spinjump |
| `05` | Killed by lava/mud |
| `06` | Turned into a coin by the goal tape |
| `07` | Inside Yoshi's mouth |
| `08` | Normal routine |
| `09` | Stationary / carryable |
| `0A` | Kicked |
| `0B` | Being carried |
| `0C` | Turned into a powerup by the goal tape |

## Core mechanic

Treat `{02,03,04,05}` as one absorbing **dead-set** and count a **transition
from a live state into the dead-set**, tracked per slot. This single rule
handles the main worries for free:

- **No double-counting** — once a slot is in the dead-set, further shuffling
  *within* it (e.g. spinjump `04` → falling `02`) does not re-count. Only the
  crossing fires.
- **Offscreening excluded** — scrolling a sprite away sends its status to `00`,
  not into the dead-set.
- **`02` (killed and falling off screen) still counts once** — the *entry* into
  `02` is the live→dead crossing.

## v1 scope

**In:** dead-set `= {02,03,04,05}` only. Neutral tally display. Text label
(no icon yet). Default disabled.

**Out (deferred; observation decides):** counting `06`/`0C` (goal-tape) and `07`
(Yoshi mouth); Tally/Passivist style toggle; per-death-state checkboxes; any
sprite-ID / "is it really an enemy" filtering (e.g. P-switch exclusion);
Destruction concept; final name and icon.

Deliberately **no filtering** in v1. Pressing a P-switch can itself register as
`03`, and other sprites may surprise us — but hardcoding a sprite-ID blacklist
is the wrong first move. Ship the raw count, observe what it actually does, and
let evidence decide whether filtering is needed and, if so, as a *general*
mechanism rather than a growing list of special cases.

## Implementation

New file `src/SMWCounters/Counters/KillCounter.cs`, a plain `ISmwCounter`
modeled on `JumpCounter` (not `BankedCounter` — no banking, no alert in v1).

Constants / offsets:

- `GameModeOffset = 0x0100`, `LevelMainMode = 0x14` (same in-level gate as
  `PowerupCounter`).
- Sprite-status base `0x14C8`, 12 slots (`0x14C8`–`0x14D3`).
- Dead-set members `0x02, 0x03, 0x04, 0x05`.

State: 12 `PreviousByte` instances, one per slot.

`Poll(memory)`:

1. If `!memory.IsAttached`: clear all 12 `PreviousByte`, return.
2. Read `$0100`; if read fails or `!= 0x14`: clear all 12, return.
3. For each slot `i` in `0..11`:
   - Read `$14C8 + i`. If the read fails, clear that slot's `PreviousByte`
     and skip it.
   - Let `cur` = value. If the slot has a previous sample and
     `prev ∉ dead-set` and `prev != 0x00` and `cur ∈ dead-set`: `Value++`.
   - `Set(cur)`.

Identity / display:

- `Id => "kills"` (stable serialization key).
- `DefaultLabel => "Kills"`.
- `DefaultIcon => null` (both the `Update` cache path and `DrawGeneral` already
  fall back to drawing `DefaultLabel` text when the icon is null). A Mario/enemy
  icon will be added later.
- `ValueIsAlert => false` (neutral number, like Jumps).
- `SetValue` / `Value` / `Reset` as in `JumpCounter`; `Reset` also clears all 12
  `PreviousByte`.

Persistence:

- `SaveState` writes `<Kills>` = `Value` (mirrors `JumpCounter`'s single
  element).
- `LoadState` reads it back (default `0`) and clears all 12 `PreviousByte`.

Wiring in `SmwCountersComponent`:

- Add `new KillCounter()` to the `counters` array in the constructor.
- Everything else is inherited from the existing loops: the settings row
  (enable checkbox + value box + reset button — `BuildExtras` returns
  `(null, null)` for this type), the poll loop, the timer-active gate with
  inert-memory flush, reset-on-splits-reset, the reset hotkey, and
  save/load round-tripping.
- **Default disabled:** it is not added to the initial enabled set
  (`{ "deaths", "exits" }`), matching jumps/moons/powerups.

## Tests

`test/SMWCounters.Tests/KillCounterTests.cs`, using `FakeSnesMemory`, written
TDD-first:

1. Live→dead (`08` → `04`) counts once; remaining in `04` on the next poll does
   not re-count.
2. Within-dead-set shuffle (`04` → `02`) does not double-count.
3. Offscreen (`08` → `00`) does not count.
4. Goal-tape (`08` → `06`) and Yoshi (`08` → `07`) do not count.
5. Entry-from-empty (`00` → `02`) does not count.
6. Multiple slots entering the dead-set in one tick each count.
7. Game mode `!= 0x14` gates counting off and clears edges; a subsequent
   in-level dead-state does not bridge to the pre-gate sample. Detach clears
   edges the same way (no bridged phantom kill after re-attach).

## Known limitation (to document when the feature firms up)

The status table cannot see *non-lethal* harm — stomping a Koopa turns it into a
live shell (`09`), never a dead state — so this counts kills, not "enemies you
disturbed." That gap matters for a strict passivist interpretation and should be
called out wherever the feature is eventually described.
