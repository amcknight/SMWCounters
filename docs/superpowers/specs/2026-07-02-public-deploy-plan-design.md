# Public Deploy Plan — LiveSplit.SmwCounters

**Date:** 2026-07-02
**Status:** Approved design, pending implementation plan

## Goal

Make LiveSplit.SmwCounters trivially usable by the broader SMW speedrunning
community: **download a DLL, drop it into `LiveSplit/Components/`, add it to the
layout.** No cloning, no .NET toolchain, no LiveSplit source tree.

This spec covers **tier 1 (self-serve download)** in full, and records tiers 2–3
as explicitly deferred.

## Background / current state

- The component is a standalone LiveSplit layout component that reads SNES WRAM
  from a running emulator and displays SMW counters (deaths, exits, jumps,
  3-up moons).
- It was extracted from a LiveSplit super-repo. The `.csproj` has a
  `ProjectReference` to `LiveSplit.Core.csproj` resolved via `$(LsSrcPath)`,
  which points into a **parallel super-repo the author still develops in**.
- **Missing for public use:** no README, no LICENSE, no `.gitignore`, no
  GitHub Release, no prebuilt DLL, no CI. Building today requires the entire
  LiveSplit source tree.

### Distribution landscape (researched)

- Third-party **game-specific** LiveSplit components are distributed as
  standalone DLLs via the author's **GitHub Releases** (Dark Souls, Skyrim,
  PoE2 autosplitters all do this). User drops the DLL in `Components/` and adds
  it via Edit Layout.
- The **in-app Component Store** (`livesplit.org/update/update.xml`) contains
  **only general-purpose components** (Splits, Timer, Counter, Sum of Best,
  etc.). No game-specific components exist there. A PR to list this component
  there would be out-of-scope and is **not** a target.
- The realistic "more official" tier is **community resource listings**
  (speedrun.com / SMWCentral resource pages, the `livesplit-component` GitHub
  topic), not the store.

## Non-goals

- No in-app Component Store integration (won't be accepted; see above).
- No community promotion push yet (deferred to post-SGDQ; see Deferred).
- No behavior/feature changes to the component itself.
- No auto-update mechanism.

## Design

### 1. Decouple the build from the LiveSplit source tree

**Approach: reference the compiled `LiveSplit.Core.dll` (conditional).**

Rationale: every LiveSplit type the code uses lives in `LiveSplit.Core.dll`
(`LiveSplit.UI`, `LiveSplit.Model`, `LiveSplit.Model.Input`,
`LiveSplit.ComponentUtil`, `LiveSplit.UI.Components`, `LiveSplit.Options`).
The project already treats Core as provided-at-runtime
(`Private="false" ExcludeAssets="runtime"`), so a compile-time-only `<Reference>`
is behaviorally equivalent.

Make the dependency conditional so the author's super-repo workflow is preserved:

- **When `LsSrcPath` is defined** (super-repo dev): keep the existing
  `ProjectReference` to `LiveSplit.Core.csproj`.
- **Otherwise** (standalone repo / CI / casual builder): use
  `<Reference Include="LiveSplit.Core">` with a `HintPath` to a
  `LiveSplit.Core.dll` obtained from a **pinned LiveSplit release**.

The pinned DLL is **not committed**; CI downloads a specific LiveSplit release
zip and extracts `LiveSplit.Core.dll` into a known `lib/` path before building.
A documented local path lets casual builders drop the DLL in the same spot.

Pin the LiveSplit version in one place (e.g. a `LiveSplitVersion` MSBuild
property) referenced by both the CI download step and the README.

**Risk / mitigation:** if the compiler needs additional referenced assemblies
(types surfaced in public signatures we touch), add those DLLs to the same
`lib/` fetch. Verified low-likelihood — no cross-assembly types are used
directly.

### 2. Repo hygiene

- **`.gitignore`** covering `artifacts/`, `bin/`, `obj/`, `lib/` (fetched DLLs),
  and standard VS noise. Remove the stray committed/untracked `artifacts/obj/`.
- **`LICENSE`** — MIT (see Licensing below).
- **`CREDITS.md`** — extend to attribute:
  - kaizosplits (`amcknight/kaizosplits`, the author's own prior project) as the
    origin of the emulator-detection and WRAM-offset research.
  - The existing Nintendo sprite-asset fan-use note.

### 3. README

New `README.md` at repo root:

- **What it is** — one-line description + a screenshot (placeholder to be
  captured; a real screenshot is a nice-to-have, not a blocker for first tag).
- **Install** — download `LiveSplit.SmwCounters.dll` from the latest Release →
  place in `LiveSplit/Components/` → in LiveSplit, Edit Layout → add
  **"SMW Counters"**.
- **Requirements** — a running supported emulator: RetroArch, snes9x
  (incl. `-x64`, `-rr`), bsnes, higan, BizHawk (`emuhawk`); a LiveSplit version
  compatible with the pinned `LiveSplit.Core` (state the version).
- **Configuration** — per-counter enable toggles, moon dedupe modes
  (All / Per level / Per room), reset key, reset-on-splits-reset,
  label overrides, alignment/row height.
- **Build from source** — both paths: super-repo (`LsSrcPath`) and standalone
  (drop `LiveSplit.Core.dll` in `lib/`, `dotnet build -c Release`).

### 4. Release automation (GitHub Actions)

`.github/workflows/release.yml`:

- **Triggers:** push of a tag matching `v*.*.*`, plus `workflow_dispatch`
  (so a build can be cut on demand for the SGDQ demo).
- **Steps:** checkout → setup .NET / MSBuild (Windows runner, net4.8.1) →
  fetch pinned `LiveSplit.Core.dll` into `lib/` → `dotnet build -c Release` →
  package `LiveSplit.SmwCounters.dll` + `README.md` + `LICENSE` + `CREDITS.md`
  into `LiveSplit.SmwCounters-<version>.zip` → create/attach GitHub Release with
  that asset.
- The release asset **must include the bare `.dll`** (some users grab just the
  DLL) as well as the zip, or the zip only with clear contents — decide during
  implementation; default to shipping both the loose `.dll` and the zip.

### 5. Versioning

- Add version attributes to `AssemblyInfo.cs` (currently only
  `InternalsVisibleTo`), sourced from an MSBuild `<Version>` so the assembly and
  the release tag agree.
- Tag scheme `vMAJOR.MINOR.PATCH`, first public tag **`v0.1.0`**.

## Licensing

- **Code:** MIT.
- **kaizosplits port:** author's own prior work → no external obligation;
  attributed in CREDITS as a courtesy and for provenance.
- **Nintendo sprite assets:** noted in CREDITS as fan-community iconography; the
  MIT license covers the code, not Nintendo's IP. README/CREDITS make this
  explicit so redistributors understand the boundary.

## Deferred (recorded, not in this implementation)

- **Tier 2 — community listings:** add `livesplit-component` (and `smw`,
  `speedrun`) GitHub topics; create a speedrun.com / SMWCentral resource entry.
  Low effort; do after a first tag exists.
- **Tier 3 — promotion:** demo in the SGDQ backrooms → recruit testers →
  SMWCentral thread. Post-SGDQ.

## Success criteria

1. A non-developer can install the component from a GitHub Release with only
   LiveSplit + an emulator installed — no toolchain.
2. Pushing a `v*.*.*` tag produces a GitHub Release with the DLL asset
   automatically.
3. The author can still build in the super-repo unchanged (`LsSrcPath` path).
4. A fresh clone of *only this repo* builds Release (with the pinned
   `LiveSplit.Core.dll` fetched), with no LiveSplit source tree present.
5. Repo has README, LICENSE (MIT), CREDITS, and `.gitignore`; no build output
   tracked.
