# SMWCounters — Open Improvements & Threads

Running list of ideas and unfinished threads, captured at the v0.2.0 cut. Not
scheduled — a parking lot to pull from. Grouped by theme, roughly high-impact
first within each group.

## Architecture / blockers for 1.0

- **SNESOffset detection.** The offset/attach tables are ported verbatim from
  kaizosplits and duplicated here. A shared, self-updating SNES memory/offset
  layer (detect emulator + core + version robustly, one source of truth across
  projects) is the gate for calling this **1.0**; until then we stay on 0.x.
  This is the main reason 1.0 is deferred.

## Counting semantics (SMW judgment calls)

- **Yoshi as a powerup.** Whether/how having Yoshi counts toward a low% /
  "least powerups" tally. Unresolved design (like the powerup-collect debate);
  needs a rule that's clean and ungameable.
- **Checkpoint (midway) as a powerup.** A midway that makes Mario big is
  effectively a powerup, but it does **not** currently increment the Powerups
  counter — the collect fires on the `$0071` grow *animation* (→2/3/4), and the
  midway grow doesn't go through that path. Future: detect a midway that raises
  `$0019` (powerup state) and optionally count it, with the earlier-discussed
  "only count it if it actually made Mario big" toggle (skip patched midways
  that do nothing). Hack-dependent; needs live-case work.
- **Three-tier banking: Finish → Exit → Save.** Today the Exit counter collects
  on the Finish (goal/orb/key/boss) and banks on the Exit write (`$1F2E`); a
  death before the Exit reverts. A fuller model adds a **Save** tier: revert on
  **game over** before the game actually saves (SRAM / castle prompt), separate
  from the die-before-Exit revert. Could render stacked with distinct colors
  (e.g. orange on Finish, yellow on banked-Exit, white on Save). Needs game-over
  detection + a two-level banked model + multi-color rendering.
- **Weighted powerup counting (Cape/Fire = 2).** Parked. The rationale (Fire
  "contains" two powerups) is shaky since a hit while Cape/Fire appears to drop
  straight to Small, not Big, and may be hack-dependent. Revisit only with live
  cases; if done, a cape↔fire swap must be +0 (lateral), +2 only when rising
  from Big-or-below.

## In-level gating (consistency)

- **Extend the game-mode gate to Jumps and Moons.** The Powerups counter now
  gates its collect on game mode `$0100 == 0x14` (fixed counting in a custom
  Yoshi House). **Jumps and Moons still gate on `$1935 == 1`** and have the same
  blind spot in that level type. Jumps especially are worth switching (you jump
  in a Yoshi House); Moons less so. Straightforward: mirror the powerup fix.

## UI / UX

- **Overflow handling.** When the enabled counters are wider than the available
  layout width, either **wrap to a second line** (preferred; keeps chosen size)
  or **shrink to fit** (one-line look). Propose wrap-by-default with a
  "shrink instead of wrap" toggle. Touches `DrawGeneral` layout math.
- **Save/restore counter values for restarting a run.** Ideas floated: recover
  the last values after a Reset/close — e.g. show the previous values greyed in
  Settings with a "Recover" button, and/or expose it via right-click. Design
  open. (Note: a right-click **menu** would shadow LiveSplit's own context menu
  — see Dropped.)
- **User-defined / advanced custom counters.** Let users define their own
  counters from Settings (pick a WRAM address + edge/compare rule + label/icon),
  rather than only the built-in set. Larger feature; needs a small rule DSL and
  UI.
- **Author line low% nudge.** The greyed author line currently just credits
  twitch.tv/mangort. Could optionally add a short "try low%: min jumps /
  powerups" suggestion. Deemed possibly intrusive; left as credit-only for now.
- **Settings dialog is fixed-size** (not user-resizable). Minor; widen if it
  ever feels cramped.

## Known limitations (documented, not bugs)

- **Exit redo inflation.** Re-completing an already-saved exit fires the Finish
  collect but does not increment `$1F2E` (the game only counts new exits), so it
  would leave the Exit alert gold until a death or reset. Does not occur in
  normal fresh-run play.
- **Attract-demo counting.** If the LiveSplit timer is left running on the title
  screen, the SMW attract demo runs real level code and could count. The primary
  guard is "only count while the timer runs."

## Dropped / parked (decided against, recorded for context)

- **Right-click value readout via `ContextMenuControls`.** A LiveSplit component
  is GDI-drawn (no hover tooltips on the overlay), so a right-click menu was the
  only path to an on-overlay "show all values incl. hidden" readout — but it
  would **shadow LiveSplit's own right-click menu**, so it was dropped. Revisit
  only if a non-conflicting mechanism appears.
- **Per-room moon dedupe.** Removed in favor of the single "One per level"
  checkbox (All vs per-level); per-room was niche.
