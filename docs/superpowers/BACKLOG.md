# SMWCounters — Backlog

Open work, grouped by what it takes to start. Entries carry the date of the
evidence they rest on; resolved entries are deleted, not struck through.
Last full refresh: 2026-09-26.

## Release-blocking (next public release)

The last public tag is v0.2.0 (2026-07-03). Coins, Kills/Destruction, the
SNES.dll structural WRAM discovery, banked histories, and the discard-on-death
column have all landed since and never shipped.

- **Screenshot.** The README still carries a TODO for a picture of the
  component in a LiveSplit layout.
- **Tag v0.6.0.** The csproj is bumped to 0.6.0 and the README matches the
  code. Pushing the tag runs the release workflow, which stages the
  `SMWCounters.dll` + `SNES.dll` zip.
- **Live smoke of the 0.6.0 changes.** Three behaviors shipped on unit tests
  alone and want one session with the debug log on: the digit-width reserve
  (watch a 9→10 rollover not shift the row), the play gate (a title-screen
  demo death and a file load must not count or bank), and "Only count when
  timer running" unticked with the timer never started.
- **Row spacing iteration.** The 2026-09-26 review found the row too spread
  out; the cell gap went 14→10px, icon-to-number 4→2px, the reserve floor
  3→2 with per-value growth by digit count. Needs another look in a real
  layout; seven counters at row height 45 still won't fit a default-width
  LiveSplit window, which is what the two-component hint is for.

## Easy wins (no live session needed)

- **Kills-row radio buttons sit at a fixed x offset in Settings.** Possible
  clipping at non-100% DPI; never confirmed. Check once at 125%/150%.
- **Settings dialog is fixed-size.** Widen or make resizable if it ever feels
  cramped.

## Decisions needed (roadmap)

Rules that need a call before any design. Each is a judgment about what a
challenge run "counts", not a technical question.

- **Yoshi as a powerup.** Whether having Yoshi counts toward a low% tally.
  Direction (2026-07-27): opt-in checkbox beside the other powerup toggles.
  Simplest rule counts each mount; the ideal rule dedupes remounts of the same
  Yoshi (identity via sprite slot is fragile across slot reuse) while a
  respawned Yoshi counts again. Needs a logged Yoshi session before design.
- **Checkpoint (midway) as a powerup.** A midway that makes Mario big does not
  increment Powerups today: the collect fires on the `$0071` grow animation
  (→2/3/4) and the midway grow skips that path. Would detect a midway raising
  `$0019` and count it, optionally only when it actually made Mario big (skip
  patched no-op midways). Hack-dependent; needs live cases.
- **Goal tape as a kill.** Two conversion paths, one setting should govern
  both. (a) Coin conversion `08->06` currently counts as a Kill (bullet bill,
  chuck confirmed 2026-07-16); a "goal tape doesn't kill" option would make
  those Destruction only. (b) Powerup conversion in some hacks despawns the
  live slot `08->00` on the goal-trigger tick (log 2026-08-04 14:25:33, still
  in mode `0x14`), so today nothing counts at all. Rule direction: key on the
  goal-trigger tick and harvest remembered live slots despawning at it. Needs
  one logged experiment with a non-goal exit as control, since `08->00` is
  also an ordinary offscreen despawn.
- **Disco-shell stomp doesn't count.** Dies from kicked status (`0A->04`),
  which the koopa origin rule excludes by design (observed 2026-07-16). Decide
  whether a Yoshi-stomped disco shell is a creature kill.
- **Weighted powerup counting (Cape/Fire = 2).** Rationale is shaky: a hit
  while Cape/Fire appears to drop straight to Small, and may be hack-dependent.
  If ever done, a cape↔fire swap is +0 and +2 only when rising from Big or
  below.
- **Reset-on-splits-reset default.** Currently on (a fresh install clears the
  counters whenever the run resets). Decide whether that is the right default
  for challenge runners who reset the timer more often than the attempt.
- **Growing the kill exclusion list.** The `NotAlive` list is hardcoded and
  evidence-driven; it grows only with `PRP`/`SPR` log citations. Decide how
  much default-list growth to do before shipping the editable list (see
  "Advanced kill config").

## Needs a logged session

Research-gated: run a session with the debug log on, then design.

- **Passivism counter.** The sprite-status table (`$14C8`) cannot see
  non-lethal disturbance (bops, fireball hits, galoomba flips, Chuck damage
  never touch the status byte), established 2026-07-14. Research path: extend
  `DebugLogger` to candidate WRAM (sprite stun timers, interaction flags) and
  observe before designing.
- **Property-based creature filter.** The blacklist keeps growing (message box
  `0xB9` counted at the goal tape) and is error-prone (`0x4B` was mislabeled;
  it is the pipe Lakitu, the rock is `0x48`). SMW copies six per-sprite
  "tweaker" bytes into WRAM per slot (`$1656/$1662/$166E/$167A/$1686/$190F`)
  with creature-adjacent bits. `PRP` log lines dump them on every death/mouth
  entry; once a few sessions exist, evaluate whether a bit predicate separates
  creatures from objects. A whitelist was rejected (silent undercounting of
  every unlisted creature).
- **Checkpoint banking: `$0DD5` false-positive check.** The level-exit event
  replaced `$1F2E` as the bank signal on 2026-07-31 and the custom-checkpoint
  entrance was seen live 2026-08-02. Still unchecked: does `$0DD5` shift on
  sublevel pipe/door transitions? If so a pipe banks early. `BNK` log lines
  answer it.
- **Custom checkpoint in a level type that never sets `$1935`.** The one
  remaining `$1935` gate is `MidwayExitBankDetector.DetectCheckpointEntrance`,
  kept on purpose to mirror kaizosplits' `CPEntrance` and suppress the
  entrance-repoint noise during level load (log 2026-08-02: `BNK cp 63->18` at
  `inLvl=00`). A Yoshi-House-style level with a custom checkpoint would not
  bank. No live sighting; revisit with a log citation.
- **Destruction tally live validation.** Kills was validated scenario by
  scenario 2026-07-16; Destruction shipped on unit tests alone (shared
  detection paths, low risk). Flip the radio for a session and spot-check
  poofs, swallows, conversions.
- **Dragon-coin persistence (Coins).** Some hacks keep dragon coins collected
  across deaths; there the Coins die-revert is wrong for that portion. Vanilla
  tracks the in-level count at `$1422`; persistence patches keep a per-level
  bitfield elsewhere. Log a candidate-WRAM session on a hack known to save
  them. Edge case; only if it grates in real runs.

## Larger features

- **Advanced kill config.** Expose the creature filter as an editable list
  behind an advanced settings surface, with a "last N kills" readout (N≈3) so
  a player can pause, see what just counted, and add it to the exclusion (or
  inclusion) list. Store lists per ROM hack, keyed by the ROM identity slug the
  connection already exposes, on top of a shared default list. Design wrinkle:
  the filter keys on the sprite number byte (`$9E`, one byte), and PIXI-based
  hacks reuse vanilla numbers there with the custom sprite number at
  `$7FAB9E`, so the readout must surface the custom number when the custom bit
  is set or entries are ambiguous. Path decided 2026-07-16: the full "not
  alive" list is impossible to complete by hand, so grow a best-effort default
  from play sessions, then ship the list as the escape hatch.
- **SA-1 hacks.** Blocked upstream: SNES.dll (snes_offsets) declines SA-1
  cartridges ("SA-1 WRAM discovery is not supported"); whether `$13/$14/$0100`
  still tick in `$7E` WRAM on SA-1 is an open empirical question there. Once
  discovery works, this repo needs a second change: the SA-1 pack relocates
  the sprite tables and widens them from 12 to 22 slots, so `KillCounter`
  needs an address/slot remap. Biggest audience gap (many modern kaizo hacks
  are SA-1), but the first step lives in snes_offsets.
- **User-defined custom counters.** Pick a WRAM address + edge/compare rule +
  label/icon from Settings. Needs a small rule DSL and UI.
- **Pending-kill gold rendering for fireball coins.** Show a fireball-converted
  creature's would-be kill in gold (like unbanked exits), revert if the coin
  despawns uncollected, settle to white on collect. Needs a "pending kills"
  count on the counter and partial-value coloring in the renderer.
- **Save/restore counter values after a Reset/close.** E.g. previous values
  greyed in Settings with a Recover button. Design open.
- **Shareable settings code.** A textbox holding a compact code for the whole
  settings choice, plus a Set button to apply one. Serialize the settings XML
  to a short string and back.
- **Overflow wrap/shrink.** When enabled counters exceed the layout width, wrap
  or shrink. Decided against 2026-07-27 because widths grow mid-run and wrap
  would change the component height mid-run; current mitigation is a settings
  hint to stack a second instance. Revisit only after digit-width stability.

## Known limitations (documented, not bugs)

- **Point-blank fireball kills can be missed.** The status transition happens
  inside one poll gap, so the edge is never sampled. Expected at the 15 ms poll
  rate; the one Kills miss that is a sampling artifact rather than a rule
  choice (2026-07-15).
- **The play gate opens at game mode `0x0C`, not at level-main.** Deaths and
  exits count from "fade to overworld" onward because the exit-flag edge
  lands at `0x0C` (log 2026-08-04) and the `$1F2E` backstop moves on the
  overworld. A hack whose title screen runs in a mode at or past `0x0C` would
  slip through; none seen.

## Dropped (decided against, kept for context)

- **Three-tier banking (Finish → Exit → Save) with game-over revert.** Dropped
  2026-09-26; not a feature this component will grow.
- **Right-click value readout via `ContextMenuControls`.** Would shadow
  LiveSplit's own right-click menu. Revisit only if a non-conflicting
  mechanism appears.
- **Per-room moon dedupe.** Replaced by the single "One per level" checkbox.
- **Author line low% nudge.** Possibly intrusive; the greyed line stays
  credit-only.
