# SMWCounters — Open Improvements & Threads

Running list of ideas and unfinished threads, captured at the v0.2.0 cut. Not
scheduled — a parking lot to pull from. Grouped by theme, roughly high-impact
first within each group.

## Counting semantics (SMW judgment calls)

- **Yoshi as a powerup.** Whether/how having Yoshi counts toward a low% /
  "least powerups" tally. Unresolved design (like the powerup-collect debate);
  needs a rule that's clean and ungameable. Direction sketched 2026-07-27:
  opt-in via a checkbox alongside the other powerup toggles. Easiest rule is
  **count each mount**; ideal rule dedupes remounts of the *same* Yoshi
  (identity via sprite slot? fragile across slot reuse) so hop-off/hop-on isn't
  double-counted, while a respawned Yoshi legitimately counts as a second.
  Needs a live observation session on Yoshi identity/slot behavior before any
  design.
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
- **Dragon-coin persistence awareness (Coins counter).** Idea 2026-07-28: some
  hacks patch dragon coins to stay collected across deaths (vanilla does not).
  In those hacks a dragon-coin get is effectively already banked, and the
  Coins counter's die-revert is wrong for that portion. Research path: find a
  tell for "this hack saves dragon coins" — vanilla tracks the in-level count
  at `$1422`; persistence patches typically maintain a per-level collected
  bitfield in extra WRAM/SRAM. DebugLogger a candidate-WRAM session on a hack
  known to save them (collect, die, watch what survives) before designing
  anything. Edge case; only worth it if it grates in real runs.
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
  layout width, either wrap to a second line or shrink to fit. **Decided
  against for v0.5.0 (2026-07-27):** counter widths grow mid-run as digit
  counts grow, so wrap would change the component's *height* mid-run —
  confusing and annoying in a LiveSplit layout. If ever revisited, it needs
  pre-emptively locked digit widths per counter. Chosen mitigation instead: a
  settings hint when many counters are enabled, suggesting a second
  SMWCounters instance stacked in the layout (ships in v0.5.0).
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
- **Shareable settings code (idea 2026-07-28).** A textbox in Settings holding
  a compact code that encodes the entire settings choice (enabled counters,
  bank toggles, row height, alignment, etc.), copyable to send to a friend,
  with a [Set] button beside it that applies an entered code. Effectively
  serialize the existing settings XML to a short string (base64/deflate or a
  custom compact form) and back. Not scheduled — wanted documented.

- **Coin tests: pin the `MaxWrapBurst` boundary and same-poll orderings.**
  Nothing asserts wrap candidate == 15 (counted) vs 16 (resync), death+collect
  in one poll, or collect+bank in one poll. Cheap facts that would lock
  `BankedCounter.Poll` ordering semantics (v0.5.0 final review).

## Known limitations (documented, not bugs)

- **`GetSettingsHashCode` doesn't hash `BankedCounter.saved`.** A bank event
  that changes `saved` but not `total` doesn't dirty the layout hash, so
  LiveSplit may not prompt to save and a reload can restore a stale `saved`
  (spurious gold alert + revert to an older bank). Pre-existing since exit
  banking; widened by each banked counter. Fix shape: expose a `StateHash`
  from `BankedCounter` like `KillCounter.StateHash` and fold it in (v0.5.0
  final review).

- ~~**Exit redo inflation.**~~ Fixed 2026-07-31: banking moved off `$1F2E` onto
  kaizosplits' Level Exit event (`$0DD5`), which fires on every level exit
  whether or not the save file already owns it. See "Checkpoint banking needs a
  live confirmation pass" below for what still wants live eyes.
- **Checkpoint banking needs a live confirmation pass.** 2026-07-31 shipped two
  new bank signals on unit tests plus the kaizosplits reference: the level-exit
  event (`$0DD5`, replacing the late `$1F2E`) and the custom-checkpoint entrance
  (`$1B403`, alongside the vanilla midway flag `$13CE`). Neither has been seen
  live yet. The open question that motivated `$1B403` — *did the reported "Jumps
  stayed gold through a midway" mean the midway flag never fired, or did Jumps
  simply re-arm the alert on the very next jump?* — is now answerable from one
  session: `DebugLogger` emits `BNK <signal> <old>-><new>` lines for all six
  banking bytes, and `CTR` lines carry an `[unbanked]` tag. Run a session with
  Debug log on, hit a checkpoint, and read which signal moved. The `$0DD5`
  false-positive risk to check at the same time: does it shift on sublevel
  pipe/door transitions? If it does, a pipe would bank early.
- **Point-blank fireball kills can be missed (sampling collapse).** When a
  fireball hits at point-blank range, the sprite's status transition happens
  inside a single poll gap, so the edge is never sampled and the kill is not
  counted. Known and expected at the 15 ms poll rate — recorded here because it
  is the one Kills miss that is a sampling artifact rather than a rule choice
  (kills/destruction v2 live-smoke, 2026-07-15).
- **Kills-row radio buttons sit at a fixed x=52 in Settings.** Flagged during
  the v2 live-smoke as a possible clipping risk at non-100% DPI scaling; never
  confirmed either way. If a user reports the Kills/Destruction radios
  overlapping their label, this is the first thing to check.
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
