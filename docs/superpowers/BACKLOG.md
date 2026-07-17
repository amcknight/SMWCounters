# SMWCounters — Open Improvements & Threads

Running list of ideas and unfinished threads, captured at the v0.2.0 cut. Not
scheduled — a parking lot to pull from. Grouped by theme, roughly high-impact
first within each group.

## Architecture / blockers for 1.0

- **SNESOffset detection.** The offset/attach tables are ported verbatim from
  kaizosplits and duplicated here. **Plan:** incorporate a generic `SNES.dll`
  memory/offset layer from another project — one source of truth that detects
  emulator + core + version robustly and finds WRAM across all emulators. This
  is the gate for calling this **1.0**; until then we stay on 0.x, and it is the
  main reason 1.0 is deferred. Adopting it also subsumes the whole
  `Offsets`/`SnesEmu` attach path here.
- **Mesen support comes via the generic `SNES.dll`.** Mesen (Mesen2, process
  `Mesen.exe`) is not supported today — it is absent from
  `Offsets.KnownProcessNames`
  (`retroarch`/`snes9x`/`snes9x-x64`/`snes9x-rr`/`bsnes`/`higan`/`emuhawk`) and
  has no WRAM offsets, so the component reports "no emulator found" and never
  attaches. This is **not** a standalone offset-research task in this repo: Mesen
  (and any other emulator) is expected to work once the generic `SNES.dll` layer
  above is adopted. Do not hand-roll per-version Mesen offsets here.

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
- **Passivism counter needs new instrumentation.** The sprite-status table
  (`$14C8`) cannot see non-lethal disturbance — bops, fireball hits, galoomba
  flips, Chuck damage never touch the status byte — so a true "did I disturb
  anything" counter is not buildable from it (established by the 2026-07-14
  observation session; see the kills/destruction v2 spec). Research path:
  extend `DebugLogger` to candidate WRAM (sprite stun timers, interaction
  flags) and observe before designing anything.
- **Editable kill-exclusion list (advanced UI).** The v2 Kills creature filter
  is a hardcoded, evidence-driven sprite-ID list. Once it has seen real use,
  expose it as an editable list (hex IDs) behind an advanced settings surface
  so per-hack custom sprites can be reclassified. Path decided 2026-07-16: the
  full "not alive" list is effectively impossible to complete by hand, so (1)
  grow a best-effort default list from play sessions of the games actually
  being run (PRP log lines make each candidate citable), then (2) expose the
  list to the user as the escape hatch for everything else.
- **"Goal tape doesn't kill" checkbox.** Creatures converted to coins by the
  goal tape (`08->06`) currently count as Kills (bullet bill, chuck confirmed
  2026-07-16). Add a kills-row setting to exclude goal-tape conversions from
  the Kills tally for players who don't consider the tape a weapon —
  status `06` entries would then count Destruction only.
- **Property-based creature filter (replace/augment the NotAlive blacklist).**
  The 2026-07-16 session showed the blacklist will keep growing (message box
  `0xB9` counted at the goal tape) and is error-prone (`0x4B` was mislabeled
  "chuck rock"; it's the pipe-dwelling Lakitu — the rock is `0x48`). SMW copies
  six per-sprite "tweaker" property bytes into WRAM per slot
  (`$1656/$1662/$166E/$167A/$1686/$190F`) with creature-adjacent bits
  ("inedible", "don't turn into a coin when goal passed", …). The DebugLogger
  now emits `PRP` lines dumping these bytes on every death/mouth entry; once a
  few sessions of data exist, evaluate whether a bit predicate separates
  creatures from objects. Until then, grow the blacklist only with `PRP`/`SPR`
  log citations. Whitelist was considered and rejected (worse: silent
  undercounting of every unlisted creature).
- **Yoshi insta-eat rule — `$160E` is the lead.** Tonight's `YOS` lines show
  Yoshi's per-slot `$160E` byte holds the tongue-target slot index (`FF` idle,
  `FF->06` while grabbing slot 6). Insta-eaten sprites (piranha, koopaling,
  spiny/pipe-lakitu eats) despawn without ever reaching mouth status 07, so the
  rule is likely: target of an active tongue despawns 08->00 ⇒ eaten. `$18AC`
  (swallow timer) is a noisy frame counter — probably only useful as a
  confirmation edge, not a trigger. Needs one focused Yoshi session + follow-up
  plan (v2 spec, "Yoshi insta-eat coverage").
- **Disco-shell stomp doesn't count (koopa origin rule).** A disco shell dies
  from kicked status (`0A->04`), which the koopa origin rule excludes from
  Kills by design (observed 22:42:11, 2026-07-16). Debatable whether a
  Yoshi-stomped disco shell "is" a creature kill; revisit if it grates.
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
- **Pending-kill gold/white rendering for fireball coins.** Idea from the
  2026-07-16 session: when a creature is fireball-converted, show the would-be
  kill in gold (like unbanked exits), revert it if the coin despawns
  uncollected, and settle to white when the coin is collected. Requires the
  counter to expose a "pending kills" count and the renderer to color partial
  values; pairs naturally with the existing exit banking colors.
- **Author line low% nudge.** The greyed author line currently just credits
  twitch.tv/mangort. Could optionally add a short "try low%: min jumps /
  powerups" suggestion. Deemed possibly intrusive; left as credit-only for now.
- **Settings dialog is fixed-size** (not user-resizable). Minor; widen if it
  ever feels cramped.

## Known limitations (documented, not bugs)

- **Exit redo inflation.** Re-completing an already-saved exit fires the Finish
  collect but does not increment `$1F2E` (the game only counts new exits), so it
  would leave the Exit alert gold until a death or reset. Does not occur in
  normal fresh-run play. The mirror image (observed live 2026-07-14): on a save
  file that already owns the exits, collected exits never bank and a later death
  reverts them to 0 — the counter appears "stuck at 0" when replaying owned
  exits. Working as designed for fresh runs; confusing on replayed saves.
- **Destruction tally is not live-validated.** The 2026-07-16 sessions
  validated the Kills tally scenario-by-scenario; Destruction shipped on unit
  tests alone (shared detection paths, so risk is low). Flip the radio to
  Destruction for a session and spot-check poofs/swallows/conversions.
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
