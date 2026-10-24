# 1-Ups counter — collected 1-Up mushrooms, plus adjust hotkeys

Draft 2026-10-24, not yet approved. Open questions for the author are in §10;
every other choice is either the author's (marked **decided**) or the
designer's call (marked **call**). Theme: a counter for the 1-Up mushrooms a
level offers as collectibles — not lives gained — detected as "a 1-Up sprite
vanished and a life arrived", with +1/−1 global hotkeys so a runner can
correct the rare miscount mid-run.

## 1. Intent

The author runs "true 100%" of ROM hacks (currently Scrap Diary) and wants to
count the 1-Up mushrooms the level places for the player: sitting in the
open, out of a block, dangled by Lakitu, hatched from an egg. Lives gained
by other means (100 coins, the fifth Dragon Coin, moons, bounce chains, the
bonus game) are explicitly not the thing. The game keeps no per-level record
of collected 1-Ups, so unlike Dragon Coins this has to be inferred from the
sprite tables while the collect happens.

**Decided:** the criterion is "a 1-Up mushroom sprite was collected", kept
clean. Cases the criterion gets wrong in either direction are corrected by
hand with the new hotkeys rather than special-cased — that keeps the rule
explainable and leaves the door open for runners who *want* those cases
counted (a goal-tape 1-Up is a real collect to some).

## 2. Goals, non-goals, anti-goals

**Goals**

- Count a collected 1-Up mushroom: vanilla sprite `$78`, the vanilla
  flying golden mushroom `$7F` (Scrap Diary uses it), and Fishin' Lakitu's
  dangled 1-Up.
- Global +1 / −1 hotkeys (keyboard or gamepad) that adjust one counter
  chosen in settings, defaulting to 1-Ups.
- Banked-counter machinery present with the "Discard on death" checkbox
  defaulting **off**, exactly the Moons arrangement.

**Non-goals (decided)**

- No exclusion for the goal-tape 1-Up (a carried item converting at the
  tape). Counted by the rule; hotkey if unwanted. §4 records the
  discriminator should a checkbox ever be wanted.
- No duplicate-block detection. A duped block's mushroom is identical to the
  original at the memory level.
- No per-level immunity or per-hack configuration. If a level force-feeds
  the same 1-Up on every entry, the runner decrements, or waits for the
  dedupe follow-up in §9.
- No lives counter. The lives byte is a confirmation signal, never the
  thing counted.

**Anti-goals**

- Counting a life that arrived without a 1-Up sprite (100 coins, Dragon
  Coins, moons, stomp chains, bonus game).
- Counting a 1-Up sprite that vanished without being collected (scrolled
  off, fell away, level ended).
- A settings page that grows a checkbox per edge case. At most one or two
  checkboxes, and only if live use proves them necessary.

## 3. Game facts the design rests on

Verified 2026-10-24 against the SMWDisX disassembly (US ROM, `SMW_U.sym`
label names below) and the SMW RAM map. Addresses are WRAM offsets as the
counters read them. These are vanilla facts; a hack can change any of them,
which is what the debug log and the hotkeys are for.

| Fact | Evidence |
|---|---|
| The 1-Up mushroom is sprite `$78`; the flying golden mushroom is sprite `$7F`, an unused vanilla sprite with its own handler. Both are vanilla numbers, so no custom-sprite collision concern. | Sprite pointer tables in `bank_01.asm` (`78 - 1-Up`, `7F - Flying yellow 1-Up`); TCRF lists `$7F` among unused objects. |
| Collecting `$78` erases its slot (`$14C8,x` ← 0) and calls `GiveMario1Up`. Collecting `$7F` does the same in `BalloonKeyFlyObjs`. | `CODE_01C4BF: STZ SpriteStatus,X … TouchedPowerUp`; `CODE_01C2A6: JSR GiveMario1Up; STZ SpriteStatus,X`. |
| `GiveMario1Up` does not add a life. It spawns a "1UP" score sprite (type `$0D`, or `$0E`/`$0F` for the goal tape's 2-Up/3-Up variants) with a 48-frame timer. When the timer reaches `$2A` (6 frames later) the score sprite adds to the pending-lives byte `$18E4` and zeroes `$18E5`. The next frame the lives handler decrements `$18E4`, plays the jingle, and increments the lives byte `$0DBE`. | `GiveMario1Up` → `GivePoints` → `CODE_02ACEF` (timer `#$30`); `CODE_02AE03`; `CODE_028AB1`. |
| Therefore `$18E4` is a one-frame pulse for a single 1-Up and a 15 ms poll can miss it. `$0DBE` (current player's lives, minus one) is the durable signal, rising about 7 frames (~117 ms) after the slot empties. `$0DB4` is the per-player saved copy and is stale mid-level. | `PlayerLives` = `$0DBE`; `SavedPlayerLives` = `$0DB4`. |
| The score sprite table `$16E1` (6 slots, free slot else least-recently-used) holds the floating text's type; `$0D`–`$10` are the 1UP/2UP/3UP/5UP family and the only types that grant lives. Every sprite-mediated 1-Up (`$78`, `$7F`, Lakitu's bait) writes one **in the collect frame**, as do the fifth Dragon Coin, moons, the bonus game, and stomp chains. 100 coins writes none. The entry lives 48 frames, so a poll cannot miss its appearance. | `ScoreSpriteNumber` = `$16E1`; `CODE_02AD34`; `CODE_02ACEF`; `CODE_00F377`/`CODE_00F388`; `bank_00.asm:13089`. |
| An off-screen despawn also sets `$14C8,x` ← 0, but first clears the sprite's load-status entry (`$1938[$161A,x]` ← 0) and adds no life. Any other vanish (lava, level end) adds no life either. | `SubOffScreen` path at `bank_01.asm:6039`. |
| 100 coins increments `$18E4` directly. Dragon Coins, moons, and stomp chains go through score sprites. None involve a `$78`/`$7F` slot. | `bank_00.asm:1689`; `ScoreSpriteLives`. |
| The bonus game's 1-Ups are **cluster sprites**, not regular sprites. They never appear in `$14C8`/`$9E`. Collecting one spawns a "1UP" score sprite directly. | `CODE_01E281` (cluster spawn); `bank_02.asm:16765` (collect, `DEC BonusOneUpsRemain`). |
| The invisible 1-Up checkpoints spawn a real `$78` at Mario's position when the fourth is taken. | `TriggerInivis1Up` → `Generate1Up`. |
| Goal tape: an item carried across becomes a powerup picked by Mario's state; it becomes `$78` when it would duplicate the reserve item, or via codes `$E0`/`$F0`–`$F2` (the latter set `$1594,x` so the mushroom is a 2-Up or 3-Up). It spawns with status **`$0C`** ("goal tape powerup") and a `$20`-frame no-touch timer, then is collected through the normal powerup path. | `LvlEndPowerUp` (`bank_00.asm:14176`); `HandleGoalPowerup`. |
| Fishin' Lakitu is plain Lakitu `$1E` whose init copies bit 4 of its X-position low byte into `$151C,x`. Nonzero means fishing: the main routine draws the line and bait itself (no `$78` slot), and when Mario is within `$18` px of the bait it zeroes `$151C,x` and spawns a "1UP" score sprite at Mario. Lakitu's slot stays alive. Only Forest of Illusion 4 in vanilla. | `InitLakitu` falls through to `InitMontyMole` (`AND #$10 : STA $151C,x`); `CODE_02E67A`; `CODE_00F388`. |
| Yoshi eggs: a block's egg is loaded with `$78` instead of Yoshi `$35` when a Yoshi is loose (`$18E2` nonzero — set when a Yoshi exists in the level or Mario enters riding one) or a baby Yoshi `$2D` is already present. Star World-style eggs (`$187B,x`) hatch into `$78` instead of baby Yoshi `$2D` under the same flag. So yes: baby Yoshis become 1-Ups when a Yoshi is around, and it is a real `$78`. | `DATA_0288A1` (`$35,$78`) at `bank_02.asm:1247`; `YoshiEgg` at `bank_01.asm:16051`. |
| Yoshi's tongue: the "inedible" tweaker bit (`$1686` bit 0) is **clear** for `$78` (`$08`) and `$7F` (`$28`), and the swallow code routes `$78` to the generic swallow, not the powerup branch. Whether the tongue actually takes a 1-Up and whether a swallow grants a life is a live test. | `Sprite1686Vals` row `$78`; `bank_01.asm:4657`. |
| Sprites spawned by blocks take the highest free slot and inherit a **stale** `$161A` (level sprite-list index): `ZeroSpriteTables` never touches it. Level-loaded sprites get a real index. Block-emitted powerups carry the rise timer `$1540,x` for their first frames; reserve-box drops carry `$1534,x`. | `GenSpriteFromBlk`; `ZeroSpriteTables`; `PowerUpRt`. |
| A "green star block that gives a 1-Up for 30 coins" does not appear in the vanilla block code; it is presumably the hack's. If it emits a `$78` it counts by the rule. | No `PlayerCoins` compare in `bank_00.asm` block handlers. |

## 4. The detection rule

Gate: attached and game mode `$0100 == $14` (level-main), as every collect
counter does. Outside the gate all per-slot and window state clears, so a
level load, the overworld, or a detach can never bridge a stale sample to a
fresh one.

**Candidate: a 1-Up sprite vanished.** For each of the 12 sprite slots,
keep the previous poll's sprite number (`$9E+i`) and status (`$14C8+i`). A
slot is a candidate when the previous poll held number `$78` or `$7F` with
status `≥ $07` (in Yoshi's mouth, normal, or the goal-tape `$0C`), and this
poll shows status `$00` **or** a different sprite number (the slot was
reused inside one poll gap). Status transitions into the dead set
(`$02`–`$06`, e.g. lava) are not candidates; nothing lives through them
either way.

**Candidate: Lakitu's bait was taken.** A slot whose previous poll held
number `$1E` with status `$08` and `$151C+i ≠ 0`, and this poll holds
number `$1E` with `$151C+i == 0`. (Lakitu dying while fishing leaves the
slot via a dead status, not via this edge, and a respawned Lakitu never
fishes — both correct non-counts.)

**Confirmation: the game acknowledged a 1-Up.** Each candidate arms a
per-slot window of `W` polls. A confirmation signal is sampled every poll;
when it fires `n` times, the `n` oldest armed windows are confirmed and each
adds one collect. A window that expires unconfirmed is dropped silently (to
the log, not the count). **Call (from the builder check):** the candidate
edge, the window, and the confirmation signal are three separable pieces, so
swapping or disabling the signal is a small change, not a rewrite. Two
signals are specified; §10 asks the author which is primary, and the live
pass measures both:

- **Lives** — `$0DBE` rose by `n`. Lag ~7 frames (§3) ≈ 8 polls at 15 ms,
  so **`W = 15`** (~225 ms) with margin for jitter and straddled reads.
  Inert in a hack that pins lives at 99 or patches the lives system out,
  which kaizo hacks commonly do; also blind at the 99 cap, and fooled by a
  100th coin inside the window.
- **Score sprite** — a `$16E1` slot took a value in `$0D`–`$10` that it did
  not hold on the previous poll. Written in the collect frame, so the window
  covers only a straddled read: **`W = 3`**. Works whether or not lives move,
  and 100 coins cannot fool it. Fooled only by another 1UP-family text
  (fifth Dragon Coin, moon, stomp chain) appearing within ~45 ms of an
  unrelated `$78` scrolling off.

Designer's recommendation: score sprite as the primary signal, lives traced
alongside in the log as the cross-check. It is the tighter window and the
one that survives infinite-lives hacks.

**Decided:** this confirmation-window shape is the same mechanism the Kill
counter uses for fireball coins, and the author accepts it for now with the
intent of replacing both later if a cleaner signal turns up. **Call:** the
window logic is extracted into one small shared class (arm a slot, tick,
confirm the oldest `n`, expire; `Clear()`), and `KillCounter` adopts it for
`coinWindow` as a behavior-preserving refactor with its existing 4-poll
constant. Test coverage for the class comes from the Kill counter's existing
fireball-coin tests plus the new counter's.

**What the rule yields against the author's list**

| Case | Result | Why |
|---|---|---|
| 1-Up sitting in the level | counts | `$78` erased, life 7 frames later |
| 1-Up from any block, flying block, invisible checkpoint set, egg-swap, 30-coin block | counts | same |
| Flying golden 1-Up (`$7F`) | counts | own handler, same erase + life |
| Fishin' Lakitu's bait | counts | `$151C` edge + life |
| 100 coins, 5th Dragon Coin, moon, bounce chain | no | no `$78`/`$7F` slot vanished |
| Bonus game | no | cluster sprites, invisible to the slot tables |
| Mushroom scrolls off / falls away / level ends | no | no life within `W` |
| Goal-tape 1-Up (status `$0C`) | counts | by decision; `$0C` before the vanish is the discriminator if a checkbox is ever added |
| Yoshi eats a 1-Up | counts iff a life arrives | the rule needs no case here; live test tells us which |
| Duped block | counts | indistinguishable; hotkey |
| Same placed 1-Up after a death | counts again (discard off) | §9 follow-up |

**Known misses and false counts (documented, not bugs)**

- With the lives signal: a pause within ~7 frames of a collect freezes the
  score sprite, the life lands after the window, and the collect is missed;
  lives capped at 99 (`$0DBE == $62`) never rise; the 100th coin inside the
  window of an unrelated off-screen `$78` despawn counts once. Hotkey for
  all three. The score-sprite signal has none of these.
- With either signal: a 1UP-family text from another source inside the
  window of an unrelated `$78` despawn counts once. Rare; hotkey.
- A hack whose 1-Up uses a custom sprite number other than `$78`/`$7F` is
  not seen. If it recurs across hacks, a small "extra 1-Up sprite numbers"
  layout setting is the fix, not a code change.

## 5. The counter

`OneUpCounter : BankedCounter`. Id `"oneups"`, label `"1-Ups"`, save name
`"OneUps"` (elements `<OneUps>`, `<OneUpsSaved>`, `<OneUpsPlain>` fall out
of the base class). Registered after Dragon Coins in the component's counter
list; hidden by default like the other collect counters added since v0.5.

Banking: `DetectBank` via `MidwayExitBankDetector`, death via the base
`DeathEdgeDetector`, exactly as Powerups and Moons. **Decided:** "Discard on
death" defaults **off**: `DefaultBankOnSave` returns false for `"oneups"` as
it does for `"moons"`, so out of the box the counter shows the plain
never-reverted history and a collect counts the instant the life lands.
Toggled on, 1-Ups go gold until a checkpoint, exit, or any non-death return
to the map banks them, and a death discards them — the low% "abort and keep
going" view.

Icon: `Assets/oneup.png`, the 16×16 1-Up mushroom sprite at the same scale
and treatment as `dragoncoin.png` (which replaced a drawn likeness with the
real sprite). **Call:** the builder ships a faithful 16×16 rendering; the
author may swap the PNG without a code change.

## 6. Hotkeys

Two new global hotkeys alongside the existing Reset hotkey, using the same
`CompositeHook` plumbing, the same `CaptureKey` click-to-set text boxes, and
the same gamepad support for free:

- **Increment hotkey** and **Decrement hotkey**, `KeyOrButton`, default
  `None` (unbound). Registered in `RegisterHotKeys`, dispatched in the
  component's `Hook_KeyOrButtonPressed` next to the Reset branch.
- **Hotkey target**: a dropdown listing every counter by its label, default
  **1-Ups**. **Decided:** one target at a time, no modifier scheme. The
  dropdown holds counter ids and shows labels, so the serialized value is
  the stable id.

An adjustment calls the target's `SetValue(Value ± 1)`, clamped at 0. For a
banked counter `SetValue` writes all three tallies, so the correction banks
immediately — **call:** a human correction is not a game event a death should
undo. For Kills it adjusts the displayed mode's tally, which is what the
runner is looking at. The hotkeys work in every timer phase and regardless
of "Only count while timer running", like Reset — **call:** the natural
moment to fix a count is while paused. After an adjustment the settings
value box is refreshed through the existing `RefreshValueBoxes` path (hook
callbacks arrive off the UI thread; marshal as `CaptureKey` already does).

**Call (from the builder check):** in `TimerPhase.Ended` the overlay is
pinned by `EndedDisplayFreeze` at the final-split values. A hotkey
adjustment re-captures the frozen value for the adjusted counter so the
correction shows; "it missed one on the last level" right after the final
split is the most likely use of the hotkeys. Reset keeps its current
behavior; changing it is out of scope.

Persistence: `IncrementKey`, `DecrementKey`, `HotkeyTarget` join the settings
XML and `GetSettingsHashCode`, so a rebinding dirties the layout
(`SettingsHashTests` gains cases). Missing elements load as unbound and
`"oneups"`.

## 7. Debug log

A logged session must be able to show, for each candidate, whether it
confirmed and how many polls it waited, because that is how `W` gets tuned
and how a miss gets diagnosed. Additions:

- `lives=` (`$0DBE`) joins the `CTR` context so every counter tick records
  the lives byte.
- A `1UP` line per candidate (`slot`, sprite number, previous status or
  `lakitu`), per confirmation (which signal, `polls=` waited), and per
  expiry. Exact format is the builder's; the three events, the signal, and
  the poll count are the requirement.
- **Call (from the builder check):** these lines come from the counter, not
  from the logger re-deriving the rule. The shared window class takes an
  optional trace sink (an `Action<string>` is enough) that the component
  wires to the logger when the debug log is on; the Kill counter's coin
  windows get the same trace for free. The logger stays a passive observer
  for everything else.
- The score sprite table `$16E1` (6 bytes) gets a transition trace of its
  own (`SCR slot<n> <old>-><new>`), so the live pass can show both
  confirmation signals against each candidate whichever is primary.
- **Call:** the candidate line also carries `$161A`, `$1540`, and `$1534`
  for the slot, and a first-sighting line for any `$78`/`$7F` records the
  same three bytes. That makes the §8 live pass double as the §9 gate — one
  session instead of two — at the cost of three hex fields.
- `$151C` for a slot holding `$1E` is worth an `SPR`-style trace while the
  Lakitu rule is unverified; drop it after the live pass if it is noisy.

## 8. Live pass owed (BACKLOG entry on merge)

Debug log on, one session. First question of the session: does `lives=`
ever change in this hack, and does a `SCR` 1UP-family entry appear on a
collect? That decides which confirmation signal is live (§4). Then, in this
order where the hack allows: a placed
1-Up; a block 1-Up; a flying golden 1-Up (Scrap Diary); Fishin' Lakitu in
Forest of Illusion 4 (author knows where to test); Yoshi's tongue on a 1-Up,
then a swallow; a goal-tape 1-Up (expected to count); the bonus game
(expected not to); 100 coins, a fifth Dragon Coin, a moon, and a bounce
chain (none count); a 1-Up left to scroll off (no count); collect, die,
re-collect with discard off (counts twice) and with discard on (once, gold
until banked); +1/−1 hotkeys while paused and while running. Read the `1UP`
lines for `polls=` to confirm `W` and the `SPR` trace for any `$78` vanish
that produced no candidate.

## 9. Follow-up, not in this build: sprite identity (unique-mushroom dedupe)

**Decided 2026-10-24:** sprite identity machinery is potentially valuable
well beyond 1-Ups and worth an experiment of its own, but it is not a
priority for this counter. Nothing in this build may preclude it; nothing in
this build waits for it.

The 1-Ups use case is the author's standing wish to count a level's placed
1-Up once across deaths and revisits. Other candidates for the same
machinery: the BACKLOG's "Yoshi as a powerup" (count each distinct Yoshi,
today sketched as identity by slot), and any future "once per placed sprite"
rule for kills or collectibles. The game offers an identity for level-placed
sprites: `$161A,x` is the sprite's index into the level's sprite list, stable
across deaths and re-entries for the same level data. Sketch, recorded so the
counter's shape doesn't close the door:

- Identity = (room `$010B`, `$161A,x`), taken at the slot's **first
  sighting**, and only when the sprite looks level-placed: `$1540,x == 0`
  and `$1534,x == 0` (not rising from a block, not a reserve drop) and
  `$1938[$161A,x] == 1`. Block-emitted mushrooms inherit a stale `$161A`
  (§3) and must not be keyed; they count every time, which is intended.
- Identities commit when the count commits: immediately with discard off;
  at the bank with discard on, forgotten on death. This is the rule that
  keeps dedupe from fighting death-reverts, the problem that sank the Moons
  per-level dedupe (dropped 2026-08-04).
- Gate: one logged session with `$161A`/`$1540`/`$1534` at first sighting
  and at vanish for a placed 1-Up and a block 1-Up, across a death. Then a
  design of its own, scoped to the machinery rather than to 1-Ups.

## 10. Open questions for the author

1. **Confirmation signal.** Do lives actually move in Scrap Diary and the
   hacks you run, or are they pinned? Either way, §4 recommends the score
   sprite (`$16E1` 1UP-family entry appearing, `W = 3`) as the primary
   signal, with the lives byte traced as the cross-check. Agree, or keep
   lives primary?
2. **Icon:** do you have the 16×16 1-Up sprite to hand, or should the
   builder render one?
3. Anything in the §4 result table you'd flip before it's built?

## 11. What's left to the builder

Class and constant names beyond those given; the exact `1UP` log format;
the shared window class's API shape and where `KillCounter`'s pending-coin
cancellation rules (tongue grab, slot reuse) sit relative to it; settings
dialog layout for the two hotkey rows and the dropdown within the fixed
460 px width; the unit tests, which follow the `FakeSnesMemory` poll-script
pattern (`PowerupCounterTests`, `KillCounterTests`) and must cover: the
`$78` and `$7F` vanish with a life inside and outside `W`; a vanish with no
life; two vanishes and a +2 life delta; the `$0C` goal status counting; the
Lakitu `$151C` edge with and without a life; slot reuse inside one gap; the
gate clearing state; discard-off immediate count vs discard-on gold/bank/
discard; hotkey adjust clamping at 0 and banking immediately; persistence
round-trip and settings hash. README gains the counter row and a hotkeys
paragraph; BACKLOG gains the §8 live pass; the CHANGELOG-style release notes
follow the existing convention.
