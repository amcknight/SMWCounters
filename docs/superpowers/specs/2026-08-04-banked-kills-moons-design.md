# Banked Kills & Moons — gold-until-lock everywhere it fits

Design approved 2026-08-04. Theme: extend the proven "collect gold, lock at
checkpoint/exit, revert on death" banking to Kills/Destruction (default ON)
and Moons (default OFF), drop the Moons per-level dedupe, and tighten the
settings UI into a "Discard on death" checkbox column now that five of seven
counters carry the toggle.

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

## 2. Moons: banked, dedupe dropped

`MoonCounter` becomes a `BankedCounter` subclass:

- **Collect** (`DetectCollectDelta`): unchanged detection — gate on game mode
  `$0100 == 0x14` (clear previous-value state when the gate fails), read
  `$13C5`, return 1 when it increases. Delta stays 0/1: two moons inside one
  ~16 ms poll is not a real scenario.
- **Bank**: its own `MidwayExitBankDetector`, same as Jumps/Coins/Powerups.
- **Death revert**: inherited.
- **Serialization**: `Moons` + `MoonsSaved` via `SaveName => "Moons"`. Old
  layouts without `MoonsSaved` load as fully banked (existing base-class
  back-compat).

**Per-level dedupe is removed entirely**: `MoonDedupeMode`, `countedKeys`,
the "One per level" checkbox, and the legacy `DedupeMode`/`DedupePerRoom`
parsing all go. Old layouts carrying those elements load fine — the elements
are simply never read. Rationale: the dedupe served one hypothetical
challenge run; banking with default-OFF covers the realistic cases without
the dedupe-vs-revert interaction (a death after a deduped collect would have
lost the moon permanently).

Moons' extras panel in settings becomes empty; its `StateHash` becomes the
inherited `total`/`saved` mix (a one-time layout-dirty on upgrade is fine).

## 3. Kills/Destruction: dual-tally banking

`KillCounter` stays standalone (single counter, dual tally — not worth
generalizing `BankedCounter` for one case) and stays one counter (splitting
would double the sprite-table polling or force a shared-engine ordering
dependency; revisit only if Destruction earns real usage). It gains:

- `killsSaved` / `destructionSaved` alongside the existing tallies, plus
  `Banked` (default true) and `HasBankToggle => true`.
- **Poll restructure (correctness-critical).** Today the entire `Poll` gates
  on game mode `0x14`. Banking signals fire *outside* that mode — the
  2026-08-04 log shows `BNK exitMode 00->80` at `mode=0B`. New order each
  poll, mirroring `BankedCounter.Poll`:
  1. Not attached → clear everything, return.
  2. `if (!Banked)` sync both saveds to their tallies.
  3. Death edge (`DeathEdgeDetector`, runs at any mode) → revert both
     tallies to their saveds and clear in-flight evidence (`pendingCoin`,
     `coinWindow`, `mouthEntry`, `tongueLinger`) — death invalidates it.
  4. Sprite-event scan: only when game mode is `0x14` (the existing body,
     including `ClearAll` of per-slot state when the gate fails — unchanged).
     When `!Banked`, collects also advance the saveds.
  5. Bank (`MidwayExitBankDetector`, runs at any mode) → both saveds take
     their tallies.
- **Display**: `ValueIsAlert` is true when the *displayed* tally differs
  from its saved (`Mode == Kills ? kills != killsSaved : destruction !=
  destructionSaved`). Gold therefore tracks whichever tally the radio shows.
- **`SetValue`** (manual edit in settings): sets the displayed tally *and*
  its saved, like `BankedCounter.SetValue`. The other tally is untouched.
- **`StateHash`**: all five persisted values feed it (`kills`,
  `destruction`, `killsSaved`, `destructionSaved`, `Mode`), distinct mixing
  factors per slot.
- **Serialization**: adds `KillsSaved` / `DestructionSaved`; when missing
  (old layouts) each defaults to its tally — fully banked, same back-compat
  rule as every other banked counter.

## 4. Settings storage: per-counter defaults

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

## 5. Settings UI: "Discard on death" column

The per-row labeled checkbox inside the extras panel is replaced by a narrow
column: one shared header label ("Discard on death") above, and a bare
checkbox per row for every counter whose `HasBankToggle` is true, at a fixed
x past the value box. Each checkbox keeps the existing tooltip. Rows without
the toggle (Deaths, Exits) leave the cell empty. The extras panel then carries only genuinely
counter-specific controls: the Kills/Destruction radios. Exact x-offsets and
column ordering are an implementation-plan detail.

## 6. Unchanged by design

- The Ended-phase display freeze needs no changes: it reads
  `Value`/`ValueIsAlert` through the counter interface and works for the two
  newly-banked counters automatically.
- Kills/Destruction detection rules (E1–E8, creature filter, evidence ledger)
  are untouched.

## 7. Testing

- `KillCounterTests`: death reverts both tallies and clears in-flight
  windows; midway banks both; **exit-save banking fires while game mode is
  not `0x14`** (the restructure's reason for existing); alert follows the
  displayed mode; `Banked = false` keeps saveds synced; save/load round-trip
  incl. missing-saved back-compat.
- `MoonCounterTests`: banked semantics (gold → bank → revert), dedupe
  removal, legacy `DedupeMode` layouts load cleanly.
- `BankToggleTests` / settings tests: per-counter defaults (moons OFF, kills
  ON), explicit-map round-trip, legacy `BankDisabled` migration, hash folds
  the map.

## 8. Out of scope (recorded follow-ups)

- **Goal-tape conversion kills.** Cited evidence (2026-08-04 log, 14:23:31):
  on `BNK exitMode 00->80` the live slots go `08->00` during `mode=0C` —
  invisible to the current mode gate, and not a dead-set entry. Design
  direction: harvest remembered live-creature slots on the exit edge. Needs
  one control experiment (non-goal exit with enemies alive) before the rule
  is written. Own spec/plan.
- **Splitting Destruction into its own counter** (with a "poof" icon) —
  revisit if it earns usage.
- The mid-run "CP didn't lock in" report from 2026-08-03/04 is attributed to
  emulator savestate loads rewriting WRAM under the detectors; not a code
  defect. No action.
