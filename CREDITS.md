# Credits

## Sprite assets

The PNGs under `src/SMWCounters/Assets/` are sprites and
iconography from *Super Mario World* (Nintendo, 1990), used as
fan-community speedrunning iconography for their respective counters:

- `death.png` &mdash; deaths counter
- `moon.png` &mdash; 3-up moon counter
- `exit.png` &mdash; level exits counter (overworld completion marker)
- `jump.png` &mdash; jump counter (Mario jumping sprite)
- `mushroom.png` &mdash; powerups counter (Super Mushroom sprite)
- `kill.png` &mdash; kills/destruction counter
- `coin.png` &mdash; coins counter

## WRAM discovery

`lib/SNES.dll` (pinned v1.8.1) comes from the author's
[snes_offsets](https://github.com/amcknight/snes_offsets) project: structural
SNES WRAM discovery for LiveSplit consumers — no offset tables, reads only.

Earlier releases used emulator-detection and WRAM offset tables ported from
[kaizosplits](https://github.com/amcknight/kaizosplits) (the author's own
earlier LiveSplit SMW project); credit for that original offset research
belongs to that project and its upstream sources.

## Licensing note

The source code in this repository is released under the MIT License (see
`LICENSE`). That license covers the code only — the *Super Mario World* sprite
PNGs under `src/SMWCounters/Assets/` remain the intellectual property
of Nintendo and are included solely as fan-community speedrunning iconography.
