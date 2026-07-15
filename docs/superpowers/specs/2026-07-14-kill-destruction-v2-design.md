# Kills / Destruction counter — v2 design

## Purpose

Evolve the v1 "Kills" observation instrument into the multi-concept row the
original spec anticipated: one counter row with a **Kills / Destruction** mode
radio. Every rule in this design is backed by a live-play observation session
(2026-07-14, `%LOCALAPPDATA%\SMWCounters\counters-debug.log`) rather than
speculation — the v1 "ship raw, observe, let evidence decide" bet paid off.

**Passivism is dropped from v2.** The sprite-status table cannot see non-lethal
disturbance (bops, fireball hits, galoomba flips, Chuck damage never touch the
status byte), so a true "did I disturb anything" counter needs new
instrumentation. Backlogged as a research item.

## Evidence summary (what the session established)

- The v1 dead-set entry rule works for straightforward kills, but:
- **Status `07` (in Yoshi's mouth) is reversible** — spit returns the sprite to
  a live state (`07->09`), so repeatedly eating/spitting a shell recounted
  every re-eat. The dead-set is not an absorbing region while `07` is in it.
- **The goal tape counts itself** (`#7B 08->06`) on every goal crossing.
- **Items enter the dead-set**: P-switch poof (`#3E 09->04`), thrown-block poof
  (`#53 09->04`), footballs (`#1B 08->02/05`), Chuck's rocks (`#4B 08->04`),
  goal-tape conversion of 1-ups/accordion blocks (`#78`, `#C8` → `06`).
- **Identical transitions differ only by sprite ID**: P-switch poof,
  throw-block poof, and a flipped-galoomba finish are all `09->04`. Any
  creature-vs-object split must be ID-based.
- **Bare shells share IDs `04-07` with living shelled koopas** (a shell kill
  logs as e.g. `#07 0A->02`). At *death* the origin status separates them:
  live koopas die from `08`, bare shells from carryable states `09/0A/0B`.
- **Fireball kills never enter the dead-set**: the slot's sprite number flips
  to `#21` (moving coin) while status stays `08`; the coin ends `08->00`
  whether collected or timed out. Uncollected coins let the enemy respawn, so
  the user wants Kills to fire on *collection*, not conversion.
- **Swallows are observable**: auto-swallow logs `07->00`, spit logs a return
  to a live state — distinguishable despite ~15-50 ms sampling.
- **Origin status cannot classify mouth entries.** Shells idle at carryable
  `09` (eat = `09->07`), but the springboard (`#2F`) idles at `08` — its eat
  logs `08->07` and its spit `07->08`, indistinguishable *by status* from
  eating a live enemy. Only the sprite ID separates "ate a creature" from
  "picked up an item". (Also confirmed: Yoshi *can* swallow a springboard —
  two `07->00` events observed.)
- **Sampling misses one-frame states**: instantly-swallowed enemies
  (shell-less koopas, spinies, eaten koopalings) never show `07` to our poll,
  so Yoshi insta-eats are invisible. Accepted limitation.

## Design priority

**Kills accuracy comes first.** Where a heuristic must err, it should err
toward *not* counting a Kill; Destruction may tolerate looser edges. Edge
cases will always exist (the coin correlation and mouth-entry classification
are accepted risks, to be validated in play) — the goal is a good balance with a
near-perfect Kills mode, not exhaustive coverage.

## Approved decisions

1. **One row, mode radio** (Kills / Destruction), not two counters. Default
   mode: Kills. Default disabled, as today.
2. **Evidence-driven exclusion list**: hardcoded in v2, containing only sprite
   IDs *observed* to produce false kills. An editable list behind an
   advanced UI is explicitly deferred until the fixed list proves itself.
3. **Fireball asymmetry**: Kills counts on coin collection (respawn-proof);
   Destruction counts at conversion (the creature-object was destroyed) and
   ignores the coin's fate.
4. **Eating a live creature** counts immediately in both modes — the creature
   dies even if the shell is spat out later. "Creature" is decided by the
   creature filter (ID list + koopa origin rule), not by origin status —
   items like the springboard idle at `08` too.
5. **Swallowing an item** (`07->00` after a non-creature mouth entry) counts
   for Destruction only; it is never a Kill.
6. **Mode switches do not recompute history.** One shared tally; past
   increments stay, future increments follow the new mode. The adjacent reset
   button covers the "I want a clean count in the other mode" case.

## Counting rules

Terminology: `DEAD = {02, 03, 04, 05, 06}` (status `07` is *removed* from the
v1 dead-set and governed by the mouth rules). "Live origin" means previous
status not in `DEAD`, not `00`, and not `07`. All rules run only while
attached, in game mode `$0100 == 0x14`, per sprite slot.

### Base events (mode-independent detection, mode-dependent counting)

Mouth entries are classified by the **creature filter** (ID list + koopa
origin rule, defined below), not by origin status — items can idle at `08`
(springboard) just like creatures.

| # | Event | Detection | Kills | Destruction |
|---|-------|-----------|-------|-------------|
| E1 | Dead-set entry | live origin → status in `DEAD` | +1 if ID passes creature filter | +1 unless goal-tape self (`#7B` → `06`) |
| E2 | Creature eaten | live origin → `07`, ID **passes** creature filter | +1 | +1 (record mouth entry as *creature*) |
| E3 | Item pickup | live origin → `07`, ID **fails** creature filter | 0 | 0 (record mouth entry as *item*) |
| E4 | Swallow | `07 -> 00` | 0 (a creature was already counted at E2; an item is not a kill) | +1 if entry was *item* (destroyed); 0 if *creature* (already counted at E2) |
| E5 | Spit | `07 ->` any live status (`08/09/0A` observed) | 0 (clear mouth entry) | 0 (clear mouth entry) |
| E6 | Fireball conversion | sprite number flips to `0x21` while status stays `08`; pre-conversion ID passes creature filter | 0 (mark slot *pending-coin*) | +1 |
| E7 | Pending coin resolved | pending-coin slot goes `08 -> 00` | +1 if the coin counter `$0DBF` changed on the same poll (collected); 0 otherwise (timeout/offscreen → enemy respawns) | 0 (already counted at E6) |

Notes:

- The eat/spit/re-eat loop is silent for items in both modes (E3/E5 cycle) and
  counts exactly once for creatures (E2 fires on the first eat; the spat-out
  shell re-enters as an item via the koopa origin rule).
- The pending-coin mark clears whenever the slot's sprite number changes to
  anything other than `0x21` or the slot restarts (`00 -> 01/08` respawn), so
  a reused slot cannot resolve a stale pending coin.
- `$0DBF` "changed" (not "increased") so the 99→0 wrap on the 100th coin still
  registers.
- A swallow (`07 -> 00`) with **no recorded mouth entry** (attach or gate-on
  while something was already held) counts nothing in either mode —
  conservative per the design priority.

### Creature filter

Used by Kills for all counting, and by both modes to classify mouth entries
(E2 vs E3). A sprite **fails** the filter when its ID (read from `$9E + slot`
at event time; for E6 the pre-conversion ID) is on the observed-offender
list:

| ID | Observed as | Evidence |
|----|-------------|----------|
| `0x1B` | Football | `08->02` ×2, `08->05` ×3 |
| `0x2F` | Springboard | eat/spit loop `08->07`/`07->08`; swallow `07->00` ×2 |
| `0x3E` | P-switch (poof after squish) | `09->04` ×4, `0B->04` ×2 |
| `0x4B` | Rock thrown by digging Chuck (believed; verify in play) | `08->04` ×5 |
| `0x53` | Throw block (poof) | `09->04` ×2 |
| `0x78` | 1-Up (goal-tape conversion) | `08->06` ×4 |
| `0x7B` | Goal tape itself | `08->06` ×2 |
| `0xC8` | Accordion block (goal-tape conversion; hack sprite) | `08->06` ×4 |

Plus one **origin-qualified rule**: IDs `0x04-0x07` (shelled koopas — bare
shells keep the same ID) pass the filter only when the origin status is `08`
(living koopa); transitions from `09/0A/0B` are the bare shell and fail the
filter (excluded from Kills; mouth entries classify as item pickups). Sprite
IDs outside `04-07` are unaffected (the flipped galoomba `#0F` dying from
`09` still counts).

Everything not excluded counts — including goal-tape `06` conversions of real
creatures (the observed bullet bill: alive, therefore killable) and koopaling
kills (`00-03`).

Destruction mode counts regardless of the filter — its only exclusion is the
goal-tape self-conversion (`#7B` entering `06`), which destroys nothing. Items
converted by the tape (`78`, `C8`) legitimately count as destruction; the
filter's only role in Destruction is timing mouth events (count creatures at
the eat, items at the swallow).

## Implementation

All changes live in `KillCounter` plus small touches in
`SmwCountersComponent` and `DebugLogger`. No interface changes.

### KillCounter

- New `public enum KillCountMode { Kills, Destruction }` and a
  `public KillCountMode Mode { get; set; }` property (default `Kills`),
  modeled on `MoonCounter.DedupeMode`.
- Per-slot state grows from one `PreviousByte` to: previous status, previous
  sprite number (`$9E + i`), mouth-entry classification (enum/byte: none /
  creature / item, set at E2/E3 and cleared on spit/swallow/slot restart), and
  a pending-coin flag. All cleared by the existing clear-on-detach / gate-off
  / reset / load paths.
- New WRAM reads per poll: `$9E + i` per slot, `$0DBF` once (tracked with its
  own `PreviousByte`).
- `DefaultLabel` stays `"Kills"` and `DefaultIcon` stays `kill.png` for both
  modes in v2 (the settings row is named by the feature; a Destruction icon
  and mode-following label are deferred). `Id` stays `"kills"`.
- `SaveState`/`LoadState` add a `<KillMode>` element (int of the enum,
  default `Kills`) alongside the existing `<Kills>` value.

### SmwCountersComponent

- `BuildExtras` gains a `KillCounter` branch following the `MoonCounter`
  pattern: two radios "Kills" / "Destruction" in a panel, bound to
  `counter.Mode`, with a refresh action. Tooltip on the panel summarizing the
  distinction (Kills = creatures only; Destruction = anything destroyed).

### DebugLogger (validation tooling)

- Log sprite-number changes even when status is unchanged
  (`SPR slot<n> #<old>-><new> id-change | mode=..`), so fireball conversions
  are visible in future sessions.
- Add `coins=$0DBF` to the CTR context line, so coin-collection correlation
  can be checked from logs.

## Tests

Rewrite `KillCounterTests` around the new rules (TDD; `FakeSnesMemory` gains
nothing — sprite IDs are just more `SetByte` addresses). Scenarios, each in
both modes unless stated:

1. Plain kill `08->04` counts once, no recount, within-dead-set shuffle silent
   (regression from v1).
2. Offscreen `08->00` and spawn-into-dead `00->02` silent (regression).
3. Item re-eat loops add nothing in either mode — both flavors: bare shell
   (`#04-07`: `09->07->09->07...`) and springboard (`#2F`: `08->07->08->07...`,
   the observed idle-at-`08` item); a final `07->00` swallow adds
   +1 Destruction, +0 Kills.
4. Creature eat (`#05` from `08` → `07`) = +1 both modes; subsequent swallow
   `07->00` adds nothing more; spit `07->09` then shell death `09->02` =
   +1 Destruction, +0 Kills (origin-qualified koopa rule, ID `04-07`).
5. Flipped galoomba (`#0F`) `09->04` counts in both modes.
6. Each exclusion-list ID entering the dead-set = +0 Kills / +1 Destruction;
   goal-tape self (`#7B` → `06`) = +0 in both.
7. Goal burst: creature → `06` counts in Kills; item IDs don't; all except
   `#7B` count in Destruction.
8. Fireball: ID flip to `0x21` at status `08` = +1 Destruction immediately,
   +0 Kills; then `08->00` with `$0DBF` changed same poll = +1 Kills; with
   `$0DBF` unchanged = +0. Excluded-ID conversion sets no pending coin.
9. Pending coin cleared by slot reuse (`00->01->08` with a new ID) — later
   coin-ish events don't count.
10. Mode radio: same event stream, mode flipped mid-stream — increments follow
    the mode active at event time; value not recomputed.
11. Game-mode gate / detach clears all per-slot state including mouth origin
    and pending coins (no bridged events).
12. Save/load round-trips `<Kills>` and `<KillMode>`; load clears edge state.

## Known limitations (document with the feature)

- **Sampling blindness**: one-frame status excursions are invisible at
  15-50 ms polling — Yoshi insta-swallows (koopalings, spinies) don't count.
- **Coin-collection correlation is a heuristic**: an unrelated coin collected
  in the same poll a fireball coin times out would miscount (+1 Kills).
- **ID lists assume this evidence generalizes**: custom sprites reusing listed
  IDs are misclassified (e.g. a custom creature on `0x4B`). The deferred
  editable list is the escape hatch.
- **Non-lethal harm is invisible** (v1 limitation, unchanged) — hence no
  Passivism.
- **Berries are tiles, not sprites** — Yoshi eating a berry never touches the
  sprite tables, so it counts for nothing in either mode. Counting berry
  destruction would require map16 tile watching, a different instrument; not
  planned.
- **Respawns**: SMW respawns most enemies on re-entry regardless of kill
  method; only the fireball-coin case gets respawn-aware treatment because it
  was observed and cheaply detectable.

## Deferred / backlog

- Editable exclusion list (advanced settings surface) once the fixed list has
  seen real use.
- Passivism: needs disturbance instrumentation research (stun timers,
  interaction flags) — extend DebugLogger to candidate addresses first.
- Destruction-specific icon and mode-following row label.
- Exit-replay observation from this session (already-saved exits collect but
  never bank, then revert on death) → note added to BACKLOG.md known
  limitations.
