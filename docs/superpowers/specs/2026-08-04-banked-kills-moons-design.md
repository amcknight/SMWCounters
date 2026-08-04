# Banked Kills & Moons — gold-until-lock everywhere it fits

Design approved 2026-08-04 (revised same day: display-selector semantics
added, goal-exit evidence corrected). Theme: extend the proven "collect gold,
lock at checkpoint/exit, revert on death" banking to Kills/Destruction
(default ON) and Moons (default OFF), make the toggle a pure display selector
over always-tracked histories, drop the Moons per-level dedupe, and tighten
the settings UI into a "Discard on death" checkbox column now that five of
seven counters carry the toggle.

## 1. Shared pieces

**`DeathEdgeDetector` extraction.** The `$0071` rising-edge-to-`09` death
detection currently private to `BankedCounter.DetectDeath` moves into a small
shared class (own `PreviousByte`, `Detect(ISnesMemory)`, `Clear()`).
`BankedCounter` keeps its `DetectDeath` virtual but delegates to an instance;
`KillCounter` composes its own instance. Targeted extraction only, mirroring
the `MidwayExitBankDetector` precedent.

**`IBankToggleCounter` interface.** `{ bool Banked { get; set; }
bool HasBankToggle { get; } }`, implemented by `BankedCounter` (existing
members) and `KillCounter` (new). The component's per-poll settings sync
(`SmwCountersComponent.Poll`, currently pattern-matching `BankedCounter`) and
the settings UI both switch to the interface, so all toggle-carrying counters
are handled uniformly: jumps, coins, powerups, kills, moons. Exits keeps
`HasBankToggle => false` (it banks on the save write itself).

## 2. The toggle is a display selector — both histories always tracked

Extends the component's existing philosophy (all counters poll regardless of
the enabled set; the Kills/Destruction radio selects between two always-live
tallies) to the "Discard on death" toggle. Every banked counter maintains
**both** histories on every poll, regardless of the toggle:

- **Banked history**: `total`/`saved` exactly as today — death reverts
  `total` to `saved`, checkpoint/exit banks `saved = total`. Runs
  unconditionally; the current `if (!Banked) saved = total` per-poll sync is
  deleted.
- **Plain history**: a new `plain` tally that every collect advances and
  nothing ever reverts.

`Banked` only chooses what displays: `Value` is `Banked ? total : plain`;
`ValueIsAlert` is `Banked && total != saved` (plain view never shows gold).
Flipping the toggle mid-run therefore snaps the shown value to what it would
have been had the setting been that way all along — no data is forfeited in
either direction.

Serialization: `<Name>Plain` joins `<Name>`/`<Name>Saved`; when missing (old
layouts) it defaults to the loaded total. `SetValue` (manual edit) sets all
three. `StateHash` gains the plain value.

## 3. Moons: banked, dedupe dropped

`MoonCounter` becomes a `BankedCounter` subclass:

- **Collect** (`DetectCollectDelta`): unchanged detection — gate on game mode
  `$0100 == 0x14` (clear previous-value state when the gate fails), read
  `$13C5`, return 1 when it increases. Delta stays 0/1: two moons inside one
  ~16 ms poll is not a real scenario.
- **Bank**: its own `MidwayExitBankDetector`, same as Jumps/Coins/Powerups.
- **Death revert**: inherited.
- **Serialization**: `Moons` + `MoonsSaved` + `MoonsPlain` via
  `SaveName => "Moons"`. Old layouts without the new elements load as fully
  banked (base-class back-compat).

**Per-level dedupe is removed entirely**: `MoonDedupeMode`, `countedKeys`,
the "One per level" checkbox, and the legacy `DedupeMode`/`DedupePerRoom`
parsing all go. Old layouts carrying those elements load fine — the elements
are simply never read. Rationale: the dedupe served one hypothetical
challenge run; banking with default-OFF covers the realistic cases without
the dedupe-vs-revert interaction (a death after a deduped collect would have
lost the moon permanently).

Moons' extras panel in settings becomes empty; its `StateHash` becomes the
inherited mix (a one-time layout-dirty on upgrade is fine). With the toggle
defaulting OFF, Moons displays the plain history out of the box — identical
to today's behavior.

## 4. Kills/Destruction: dual-tally banking

`KillCounter` stays standalone (single counter, dual tally — not worth
generalizing `BankedCounter` for one case) and stays one counter (splitting
would double the sprite-table polling or force a shared-engine ordering
dependency; revisit only if Destruction earns real usage). It gains:

- Per tally, the section-2 triple: `kills`/`killsSaved`/`killsPlain` and
  `destruction`/`destructionSaved`/`destructionPlain`, plus `Banked`
  (default true) and `HasBankToggle => true`. Collects advance the banked
  total and the plain tally of both event streams; death revert and bank act
  on both banked histories together.
- **Poll restructure (correctness-critical).** Today the entire `Poll` gates
  on game mode `0x14`, but the bank signals fire *after* the game leaves
  level-main mode. 2026-08-04 log, real goal exit: the goal trigger
  (`fanfare=01`, tape self-conversion `7B->06`) happens in `mode=14` at
  14:25:33, yet the exit-flag edge the detector watches lands ~11 s later —
  `14:25:44.269 BNK exitMode 00->01 | mode=0C`, with the `$1F2E` backstop
  incrementing on the overworld (`mode=0E`). A whole-poll mode gate would
  swallow both. (Death exits park `$0DD5` at `0x80`, which
  `LevelExitDetector` already excludes — dying must not bank.) New order
  each poll, mirroring `BankedCounter.Poll`:
  1. Not attached → clear everything, return.
  2. Death edge (`DeathEdgeDetector`, runs at any mode) → revert both
     banked tallies to their saveds and clear in-flight evidence
     (`pendingCoin`, `coinWindow`, `mouthEntry`, `tongueLinger`) — death
     invalidates it.
  3. Sprite-event scan: only when game mode is `0x14` (the existing body,
     including `ClearAll` of per-slot state when the gate fails —
     unchanged).
  4. Bank (`MidwayExitBankDetector`, runs at any mode) → both saveds take
     their banked totals.
- **Display**: `Value` shows the displayed tally's selected history
  (`Banked ? total : plain` for whichever of Kills/Destruction the radio
  picks); `ValueIsAlert` is `Banked` and that tally's `total != saved`.
- **`SetValue`** (manual edit in settings): sets the displayed tally's
  triple. The other tally is untouched.
- **`StateHash`**: all seven persisted values feed it (both triples plus
  `Mode`), distinct mixing factors per slot.
- **Serialization**: adds `KillsSaved`/`KillsPlain` and
  `DestructionSaved`/`DestructionPlain`; when missing (old layouts) each
  defaults to its tally — fully banked, same back-compat rule as every other
  banked counter.

## 5. Settings storage: per-counter defaults

The legacy `<BankDisabled>` id set cannot express "moons defaults OFF"
(absence is ambiguous between "user enabled it" and "never seen"). Storage
becomes an explicit map:

- In-memory: `Dictionary<string, bool> bankOnSave`.
  `IsBankOnSave(id)` → explicit value if present, else the per-counter
  default: **false for `moons`, true for everything else**.
- Write: a `<BankOnSave>` element with one entry per toggle-carrying counter
  id, all explicit. (`BankDisabled` is no longer written.)
- Read: prefer `<BankOnSave>` entries; for ids absent there, a legacy
  `<BankDisabled>` membership reads as false; absent everywhere → default.
- `CombineSetHashes` folds the map (id + value pairs, commutative within the
  map, order-sensitive against the enabled set) so toggling any counter
  still dirties the settings hash.

## 6. Settings UI: "Discard on death" column

The per-row labeled checkbox inside the extras panel is replaced by a narrow
column: one shared header label ("Discard on death") above, and a bare
checkbox per row for every counter whose `HasBankToggle` is true, at a fixed
x past the value box. Each checkbox keeps the existing tooltip. Rows without
the toggle (Deaths, Exits) leave the cell empty. The extras panel then
carries only genuinely counter-specific controls: the Kills/Destruction
radios. Exact x-offsets and column ordering are an implementation-plan
detail.

## 7. Unchanged by design

- The Ended-phase display freeze needs no changes: it reads
  `Value`/`ValueIsAlert` through the counter interface and works for the two
  newly-banked counters automatically.
- Kills/Destruction detection rules (E1–E8, creature filter, evidence ledger)
  are untouched.

## 8. Testing

- `BankedCounter`-level: plain history never reverts; toggle flip mid-run
  swaps displayed value/alert both directions without losing either history;
  save/load round-trip incl. missing-`Plain` back-compat.
- `KillCounterTests`: death reverts both banked tallies (plain untouched)
  and clears in-flight windows; midway banks both; **exit banking fires
  while game mode is not `0x14`** (the restructure's reason for existing);
  death-exit `$0DD5 -> 0x80` does not bank; alert follows the displayed
  mode and toggle; round-trip incl. back-compat.
- `MoonCounterTests`: banked semantics (gold → bank → revert), default-OFF
  displays plain, dedupe removal, legacy `DedupeMode` layouts load cleanly.
- `BankToggleTests` / settings tests: per-counter defaults (moons OFF, kills
  ON), explicit-map round-trip, legacy `BankDisabled` migration, hash folds
  the map.

## 9. Out of scope (recorded follow-ups)

- **Goal-tape conversion kills.** Corrected evidence (2026-08-04 log,
  14:25:33): the goal trigger happens *in* game mode `0x14` — live slots
  despawn `08->00` on the same tick the tape self-converts (`7B->06`,
  `fanfare` 0→1). They are visible to the current KillCounter but
  `08->00` is (correctly) not a counting event — it is also what ordinary
  offscreen despawns look like. Rule direction: key on the goal-trigger
  tick and harvest remembered live-creature slots despawning at it. Today's
  log has no instance with enemies onscreen at the tape, so the rule still
  needs one logged experiment (goal crossed with live enemies, plus a
  non-goal exit as control). Own spec/plan; backlog entry filed.
- **Count-outside-active-run checkbox** — opt-in polling while the timer
  isn't running (casual/practice counting). Backlog entry filed.
- **Splitting Destruction into its own counter** (with a "poof" icon) —
  revisit if it earns usage.
- The mid-run "CP didn't lock in" report from 2026-08-03/04 is attributed to
  emulator savestate loads rewriting WRAM under the detectors; not a code
  defect. No action.
