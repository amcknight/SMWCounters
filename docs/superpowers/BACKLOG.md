# SMWCounters — Backlog

Open work in priority tiers. Each entry says what gates it: a decision, a
logged session (debug log on, then read the trail), or nothing. Entries carry
the date of the evidence they rest on; resolved entries are deleted, not
struck through. Priorities set 2026-09-26.

## Now — before tagging v0.6.0

- **Leaving a level without an exit event leaves collects gold.** Seen
  2026-09-26: the hack's intro ends on a "book" orb — Exits collected on the
  finish trigger, no level-exit event fired, and Exits sat gold on the
  overworld until a later death. A jump in the intro stayed gold into the
  next level. Start+select after a jump leaves it gold on the overworld.
  Proposed rule, from "banked = the final route's tally": leaving with the
  exit recorded banks (today); leaving without it (start+select, and any
  return to the map that doesn't record the exit) discards like a death;
  the intro finish banks, because that progress is kept — die in the intro
  and you replay it, finish it and you never see it again. Gates: (1) a
  decision on start+select, discard vs. keep pending until the next death;
  (2) one logged session to find the intro's tell and to see what side exits
  and pipes-to-map write to `$0DD5` and the game mode. The log now emits a
  `BNK mode` line on every game-mode change with `exitMode`/`lvl` context.
- **A powerup that goes to the reserve box doesn't count.** Seen 2026-09-26:
  mushroom while big, flower while fire — both went to the reserve, neither
  counted. The rule fires only on the `$0071` grow animation. Fix shape: also
  count a reserve-box (`$0DC2`) fill or upgrade, and suppress the grow that
  follows dropping the reserve (Select) so drop-and-use isn't counted twice.
  Open: a mushroom grabbed while big with a full reserve is consumed with no
  WRAM trace at all; probably accept the miss. Gate: one logged session
  (`pstate`/`reserve` are now traced).
- **Screenshot.** The README still carries a TODO for a picture of the
  component in a LiveSplit layout.
- **Live smoke still owed:** the play gate (a title-screen demo death and a
  file load must not count or bank). Confirmed 2026-09-26: the 9→10 rollover
  holds the row, counting with the timer stopped works, banking on a real exit
  locks in, re-beating a level locks in again.
- **Row spacing iteration.** 2026-09-26 review: too spread out; cell gap
  14→10px, icon-to-number 4→2px, reserve floor 3→2 with per-value growth by
  digit count. Needs another look in a real layout. Seven counters at row
  height 45 won't fit a default-width LiveSplit window; that's what the
  two-component hint is for.
- **Tag v0.6.0.** csproj is 0.6.0 and the README matches. Pushing the tag
  runs the release workflow, which stages the `SMWCounters.dll` + `SNES.dll`
  zip.

## Next — high

- **Midway that changes Mario's state counts as a powerup.** Decided
  2026-09-26: a midway that raises `$0019` (small→big) counts one powerup; a
  midway that leaves Mario as he was counts nothing. Today the collect fires
  on the grow animation and the midway grow skips it. Gate: none beyond a
  confirming log line of `pstate` moving at a midway.
- **Disco shell stomp counts as a kill.** Decided 2026-09-26: a koopaling is
  "really" inside, so it is a creature, unlike a bare shell. Today it dies
  from kicked status (`0A->04`) and the koopa origin rule excludes it
  (observed 2026-07-16). Needs the disco shell's sprite ID to be treated as a
  creature regardless of origin status. Gate: none.
- **Save/restore counter values after a Reset/close.** Recover the previous
  values, e.g. shown greyed in Settings with a Recover button. Most wanted for
  Deaths and Exits. Gate: design.

## Later — medium

- **Yoshi as a powerup.** Decided 2026-09-26: a checkbox, only if counting is
  clean. Count each intuitively distinct Yoshi, i.e. each spawn, never each
  mount. Identity by sprite slot should hold since a live Yoshi keeps his
  slot; a respawn is a new Yoshi and counts again. Gate: one logged Yoshi
  session (slot behavior across hop-off/hop-on, despawn, respawn).
- **Advanced kill config.** Editable creature filter behind an advanced
  settings surface, with a "last N kills" readout (N≈3) so a player can
  pause, see what just counted, and add it to the exclusion or inclusion
  list. Lists per ROM hack, keyed by the ROM identity slug the connection
  already exposes, over a shared default list. Design wrinkle: the filter
  keys on the sprite number byte (`$9E`), and PIXI-based hacks reuse vanilla
  numbers there with the custom number at `$7FAB9E`, so the readout must
  surface that when the custom bit is set. Large. Gate: spec.
- **Coin-conversion kills checkbox.** Medium-low. Leaning 2026-09-26: one
  checkbox for whether coin conversions count as kills, default off. The goal
  tape is the odd case because its coins are auto-collected; fireball coins
  count today only when the coin is collected. Both conversion paths (coin
  `08->06`, and the powerup-conversion despawn `08->00` seen in the
  2026-08-04 log) should sit under the one setting. Gate: decision on
  fireball coins, then one logged goal-tape experiment with a non-goal exit
  as control.
- **Verification wanted (one session each):** does `$0DD5` shift on sublevel
  pipe/door transitions (would bank early)? Destruction tally spot-check
  (shipped on unit tests). Kills-row radios at 125%/150% DPI.

## Someday — low

- **Dragon-coin counting.** Independent feature; nice for hacks that don't
  track them. Vanilla tracks the in-level count at `$1422`; hacks that save
  them across deaths keep a per-level bitfield elsewhere, which would also
  fix the Coins die-revert for that portion. Gate: logged session.
- **Pending-kill gold rendering for fireball coins.** Show the would-be kill
  in gold, revert if the coin despawns, lock in on collect; check the
  interaction with level end. Needs a pending count on the counter and
  partial-value coloring. Liked; low.
- **Passivism counter.** `$14C8` can't see non-lethal disturbance (bops,
  fireball hits, galoomba flips, Chuck damage; established 2026-07-14).
  Massive: per-enemy cases. Research path: trace sprite stun timers and
  interaction flags.
- **Tweaker-byte creature filter.** Six per-sprite property bytes
  (`$1656/$1662/$166E/$167A/$1686/$190F`) are dumped on every death/mouth
  entry (`PRP` lines). Unlikely to yield a clean rule; may improve the
  default list. Part of advanced kill config.
- **User-defined counters.** WRAM address + edge/compare rule + label from
  Settings. Only with a genuinely easy UI.
- **Shareable settings code.** Compact code for the whole settings choice
  plus a Set button. A good problem to have; after the tool has users.
- **Overflow wrap/shrink.** Revisit only now that value widths are stable;
  wrap would still change component height mid-run.
- **Settings dialog is fixed-size.** Widen or make resizable if cramped.

## Known limitations (documented, not bugs)

- **Point-blank fireball kills can be missed.** The status transition happens
  inside one poll gap, so the edge is never sampled. Expected at the 15 ms
  poll rate (2026-07-15).
- **The play gate opens at game mode `0x0C`, not at level-main.** Deaths and
  exits count from "fade to overworld" onward because the exit-flag edge
  lands at `0x0C` (log 2026-08-04) and the `$1F2E` backstop moves on the
  overworld. A hack whose title screen runs in a mode at or past `0x0C` would
  slip through; none seen.
- **Custom checkpoint in a level type that never sets `$1935`.** The one
  remaining `$1935` gate is `MidwayExitBankDetector.DetectCheckpointEntrance`,
  kept to mirror kaizosplits' `CPEntrance` and suppress entrance-repoint
  noise during level load (log 2026-08-02). A Yoshi-House-style level with a
  custom checkpoint would not bank. No live sighting.

## Dropped (decided against, kept for context)

- **Three-tier banking (Finish → Exit → Save) with game-over revert.** Dropped
  2026-09-26.
- **Cataloguing default-setting decisions.** Dropped 2026-09-26: defaults are
  regenerable from a glance at the settings dialog.
- **Weighted powerup counting (Cape/Fire = 2).** Rationale is shaky; a hit
  while Cape/Fire appears to drop straight to Small. Not pursued.
- **Right-click value readout via `ContextMenuControls`.** Would shadow
  LiveSplit's own right-click menu.
- **Per-room moon dedupe.** Replaced by the single "One per level" checkbox,
  itself since removed.
- **Author line low% nudge.** Possibly intrusive; the greyed line stays
  credit-only.
