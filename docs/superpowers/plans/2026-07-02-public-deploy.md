# Public Deploy (Tier 1: Self-Serve Download) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship LiveSplit.SmwCounters as a downloadable DLL via GitHub Releases so a non-developer can install it with only LiveSplit + an emulator — no toolchain, no LiveSplit source tree.

**Architecture:** Break the build's dependency on the LiveSplit source tree by making the `LiveSplit.Core` reference conditional — `ProjectReference` when the super-repo's `$(LsSrcPath)` is set, otherwise a plain `<Reference>` to a prebuilt `LiveSplit.Core.dll` fetched from a pinned LiveSplit release. Add repo hygiene (`.gitignore`, MIT `LICENSE`, extended `CREDITS`), a `README`, an assembly version, and a GitHub Actions workflow that turns a `v*.*.*` tag into a Release with the DLL.

**Tech Stack:** .NET SDK (net4.8.1 TFM, WinForms), MSBuild, PowerShell (fetch script), GitHub Actions (windows runner).

## Global Constraints

- **Pinned LiveSplit version:** `1.8.37` — asset URL `https://github.com/LiveSplit/LiveSplit/releases/download/1.8.37/LiveSplit_1.8.37.zip`, which contains `LiveSplit.Core.dll` at the archive root.
- **License:** MIT. Copyright holder: `Andrew McKnight`. Year: `2026`.
- **First public tag:** `v0.1.0`; tag scheme `vMAJOR.MINOR.PATCH`.
- **Author's super-repo build must keep working:** any change guarded so that when `$(LsSrcPath)` is non-empty the existing `ProjectReference` path is used unchanged.
- **Supported emulators (for docs):** RetroArch, snes9x (incl. `snes9x-x64`, `snes9x-rr`), bsnes, higan, BizHawk (`emuhawk`).
- **Component display name in LiveSplit:** `SMW Counters` (from `ComponentName`).
- **Never commit build output or fetched DLLs** (`artifacts/`, `bin/`, `obj/`, `lib/*.dll`).
- **Line endings:** repo is developed on Windows; keep default CRLF behavior, do not add a `.gitattributes` that forces LF.

---

### Task 1: Repo hygiene — `.gitignore`

**Files:**
- Create: `.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: ignore rules relied on by all later tasks (so fetched `lib/` DLLs and `artifacts/` output never get staged).

- [ ] **Step 1: Create `.gitignore`**

```gitignore
# Build output
artifacts/
bin/
obj/

# Fetched third-party assemblies (see scripts/fetch-livesplit-core.ps1)
lib/*.dll

# Visual Studio / Rider
.vs/
.idea/
*.user
*.suo

# OS noise
Thumbs.db
.DS_Store
```

- [ ] **Step 2: Verify build output is now ignored**

Run: `git status --porcelain --ignored artifacts`
Expected: the `artifacts/` path appears with a `!!` (ignored) prefix, and a plain `git status --short` shows no `artifacts/` entries.

- [ ] **Step 3: Commit**

```bash
git add .gitignore
git commit -m "Add .gitignore for build output and fetched assemblies"
```

---

### Task 2: MIT LICENSE + CREDITS attribution

**Files:**
- Create: `LICENSE`
- Modify: `CREDITS.md`

**Interfaces:**
- Consumes: nothing.
- Produces: `LICENSE` (MIT) and `CREDITS.md` referenced by the README and packaged into the release zip.

- [ ] **Step 1: Create `LICENSE` (verbatim MIT text)**

```text
MIT License

Copyright (c) 2026 Andrew McKnight

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

- [ ] **Step 2: Extend `CREDITS.md`** — append a new section after the existing sprite-assets section. The file currently ends after the `jump.png` bullet; add:

```markdown

## Offset research

Emulator detection and the SNES WRAM offset tables were ported from
[kaizosplits](https://github.com/amcknight/kaizosplits) (the author's own
earlier LiveSplit SMW project). All credit for the original offset research
belongs to that project.

## Licensing note

The source code in this repository is released under the MIT License (see
`LICENSE`). That license covers the code only — the *Super Mario World* sprite
PNGs under `src/LiveSplit.SmwCounters/Assets/` remain the intellectual property
of Nintendo and are included solely as fan-community speedrunning iconography.
```

- [ ] **Step 3: Verify**

Run: `git status --short`
Expected: `LICENSE` shown as new (`A`/`??`) and `CREDITS.md` shown as modified (`M`).

- [ ] **Step 4: Commit**

```bash
git add LICENSE CREDITS.md
git commit -m "Add MIT LICENSE and attribute kaizosplits offset research"
```

---

### Task 3: Decouple the build from the LiveSplit source tree

This is the critical task. It makes the project build from a fresh clone with no LiveSplit source present, while keeping the author's super-repo build working.

**Files:**
- Modify: `src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj:11-13` (the `ProjectReference` ItemGroup)
- Create: `scripts/fetch-livesplit-core.ps1`
- Create: `lib/.gitkeep`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: a `lib/LiveSplit.Core.dll` location and a `LiveSplitVersion` MSBuild property (default `1.8.37`) that Task 6's workflow reuses.

- [ ] **Step 1: Replace the `ProjectReference` ItemGroup with a conditional reference.** In `src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj`, replace:

```xml
  <ItemGroup>
    <ProjectReference Include="$(LsSrcPath)\LiveSplit.Core\LiveSplit.Core.csproj" Private="false" ExcludeAssets="runtime" />
  </ItemGroup>
```

with:

```xml
  <PropertyGroup>
    <!-- LiveSplit release whose LiveSplit.Core.dll the standalone/CI build references.
         Keep in sync with scripts/fetch-livesplit-core.ps1 and the release workflow. -->
    <LiveSplitVersion>1.8.37</LiveSplitVersion>
  </PropertyGroup>

  <!-- Super-repo dev: build against LiveSplit source when the super-repo provides LsSrcPath. -->
  <ItemGroup Condition="'$(LsSrcPath)' != ''">
    <ProjectReference Include="$(LsSrcPath)\LiveSplit.Core\LiveSplit.Core.csproj" Private="false" ExcludeAssets="runtime" />
  </ItemGroup>

  <!-- Standalone / CI: reference the prebuilt LiveSplit.Core.dll fetched into lib/. -->
  <ItemGroup Condition="'$(LsSrcPath)' == ''">
    <Reference Include="LiveSplit.Core">
      <HintPath>$(MSBuildThisFileDirectory)..\..\lib\LiveSplit.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
```

- [ ] **Step 2: Create `scripts/fetch-livesplit-core.ps1`**

```powershell
#requires -Version 5
<#
    Downloads a pinned LiveSplit release and extracts LiveSplit.Core.dll into lib/.
    Used by the standalone/CI build path (see the .csproj reference guarded on LsSrcPath).
#>
[CmdletBinding()]
param(
    [string]$Version = "1.8.37"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$libDir   = Join-Path $repoRoot "lib"
$dest     = Join-Path $libDir "LiveSplit.Core.dll"

New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$url     = "https://github.com/LiveSplit/LiveSplit/releases/download/$Version/LiveSplit_$Version.zip"
$tmpZip  = Join-Path ([System.IO.Path]::GetTempPath()) "LiveSplit_$Version.zip"
$tmpDir  = Join-Path ([System.IO.Path]::GetTempPath()) "LiveSplit_$Version"

Write-Host "Downloading $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $tmpZip

if (Test-Path $tmpDir) { Remove-Item -Recurse -Force $tmpDir }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force

$core = Get-ChildItem -Path $tmpDir -Filter "LiveSplit.Core.dll" -Recurse | Select-Object -First 1
if (-not $core) { throw "LiveSplit.Core.dll not found in $url" }

Copy-Item -Path $core.FullName -Destination $dest -Force
Write-Host "Wrote $dest"
```

- [ ] **Step 3: Create `lib/.gitkeep`** so the directory exists in a fresh clone (the DLL itself stays ignored via Task 1).

```text
# Fetched assemblies land here; see scripts/fetch-livesplit-core.ps1.
```

- [ ] **Step 4: Fetch the reference DLL**

Run: `pwsh -File scripts/fetch-livesplit-core.ps1`
Expected: ends with `Wrote .../lib/LiveSplit.Core.dll`; `lib/LiveSplit.Core.dll` now exists.

- [ ] **Step 5: Build the standalone path and verify the DLL is produced**

Run: `dotnet build src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj -c Release`
Expected: `Build succeeded`. Then confirm output exists:
Run: `ls artifacts/bin/LiveSplit.SmwCounters/release/LiveSplit.SmwCounters.dll` (path may vary by SDK; if not there, locate it: `find artifacts -name LiveSplit.SmwCounters.dll`)
Expected: the DLL is listed.

If the build fails with a missing type from another LiveSplit assembly, add a sibling `<Reference>` (same pattern) to that DLL — it will be present in the same extracted release folder; extend the fetch script to copy it too. This is the documented fallback from the spec's risk note.

- [ ] **Step 6: Commit**

```bash
git add src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj scripts/fetch-livesplit-core.ps1 lib/.gitkeep
git commit -m "Build against prebuilt LiveSplit.Core.dll when outside the super-repo"
```

---

### Task 4: Set assembly version

**Files:**
- Modify: `src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj` (PropertyGroup)

**Interfaces:**
- Consumes: nothing.
- Produces: a `<Version>` property; the release workflow (Task 6) reads the tag but the assembly carries this version.

- [ ] **Step 1: Add version to the first `PropertyGroup`** in the csproj (the one containing `TargetFramework`). Add these lines inside it:

```xml
    <Version>0.1.0</Version>
    <Company>Andrew McKnight</Company>
    <Product>LiveSplit SMW Counters</Product>
```

(The SDK derives `AssemblyVersion`/`FileVersion` from `<Version>`; the existing `Properties/AssemblyInfo.cs` only declares `InternalsVisibleTo`, so there is no duplicate-attribute conflict.)

- [ ] **Step 2: Rebuild and verify the version is stamped**

Run: `dotnet build src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj -c Release`
Then: `pwsh -Command "(Get-Item (Get-ChildItem -Recurse -Filter LiveSplit.SmwCounters.dll artifacts | Select-Object -First 1).FullName).VersionInfo.FileVersion"`
Expected: `0.1.0.0` (or `0.1.0`).

- [ ] **Step 3: Commit**

```bash
git add src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj
git commit -m "Set assembly version 0.1.0"
```

---

### Task 5: README

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: the emulator list, component name, and configuration options (all from Global Constraints and the component source).
- Produces: install/usage docs; packaged into the release zip by Task 6.

- [ ] **Step 1: Create `README.md`**

````markdown
# LiveSplit SMW Counters

A [LiveSplit](https://livesplit.org/) layout component that shows live *Super
Mario World* counters — **deaths**, **level exits**, **jumps**, and **3-up
moons** — by reading SNES WRAM from your running emulator.

<!-- TODO: add a screenshot of the component in a LiveSplit layout. -->

## Install

1. Download `LiveSplit.SmwCounters.dll` from the
   [latest release](https://github.com/amcknight/LiveSplit.SmwCounters/releases/latest).
2. Copy it into the `Components` folder inside your LiveSplit install
   (e.g. `LiveSplit/Components/`).
3. Start LiveSplit → right-click → **Edit Layout…** → **+** → **Information →
   SMW Counters**.
4. Save the layout.

## Requirements

- **LiveSplit** 1.8.37 or newer.
- A running, supported SNES emulator with *Super Mario World* loaded:
  RetroArch, snes9x (including `snes9x-x64` and `snes9x-rr`), bsnes, higan, or
  BizHawk.
- Counting only happens while the LiveSplit timer is running or paused, so the
  title screen / file select / demos don't pollute the counts.

## Configuration

Open the component's settings (Edit Layout → double-click **SMW Counters**):

- **Enable/disable** each counter independently (deaths and exits are on by
  default).
- **3-up moon dedupe mode** — count **All** moons, or de-duplicate **Per level**
  or **Per room**.
- **Label overrides** — replace a counter's default sprite icon with your own
  text label.
- **Reset key** — a hotkey (keyboard or gamepad) that zeroes the counters.
- **Reset on splits reset** — clear counters whenever the run resets.
- **Alignment** and **row height** for layout fit.

## Build from source

**Standalone (no LiveSplit source tree):**

```sh
pwsh -File scripts/fetch-livesplit-core.ps1   # fetches lib/LiveSplit.Core.dll
dotnet build src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj -c Release
```

The built DLL lands under `artifacts/bin/LiveSplit.SmwCounters/`.

**Inside the LiveSplit super-repo:** provide `LsSrcPath` (pointing at the
LiveSplit `src` folder) and the project references `LiveSplit.Core` by source
automatically.

## Credits & license

Code is MIT-licensed (see [LICENSE](LICENSE)). Emulator/offset research is
ported from [kaizosplits](https://github.com/amcknight/kaizosplits). *Super
Mario World* sprites are Nintendo's, used as fan iconography — see
[CREDITS.md](CREDITS.md).
````

- [ ] **Step 2: Verify** the file has no unresolved placeholders other than the intentional screenshot `TODO`.

Run: `grep -n "TODO\|TBD\|FIXME" README.md`
Expected: only the single screenshot `TODO` line.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "Add README with install, requirements, config, and build docs"
```

---

### Task 6: GitHub Actions release workflow

**Files:**
- Create: `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `scripts/fetch-livesplit-core.ps1`, the `LiveSplitVersion` pin, and the built DLL path from Task 3.
- Produces: a GitHub Release (on `v*.*.*` tag or manual dispatch) with the loose `LiveSplit.SmwCounters.dll` and a `LiveSplit.SmwCounters-<tag>.zip` (DLL + README + LICENSE + CREDITS) attached.

- [ ] **Step 1: Create `.github/workflows/release.yml`**

```yaml
name: Release

on:
  push:
    tags:
      - "v*.*.*"
  workflow_dispatch:
    inputs:
      version:
        description: "Version label for a manual build (e.g. v0.1.0-test)"
        required: false
        default: "v0.0.0-dev"

permissions:
  contents: write

jobs:
  build-release:
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET SDK
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "9.0.x"

      - name: Resolve version
        id: ver
        shell: pwsh
        run: |
          $v = if ("${{ github.ref_type }}" -eq "tag") { "${{ github.ref_name }}" } else { "${{ github.event.inputs.version }}" }
          "version=$v" | Out-File -FilePath $env:GITHUB_OUTPUT -Append

      - name: Fetch LiveSplit.Core.dll
        shell: pwsh
        run: pwsh -File scripts/fetch-livesplit-core.ps1

      - name: Build (Release)
        run: dotnet build src/LiveSplit.SmwCounters/LiveSplit.SmwCounters.csproj -c Release

      - name: Stage release assets
        id: stage
        shell: pwsh
        run: |
          $dll = Get-ChildItem -Recurse -Filter LiveSplit.SmwCounters.dll artifacts | Select-Object -First 1
          if (-not $dll) { throw "built DLL not found" }
          $out = "release-staging"
          New-Item -ItemType Directory -Force -Path $out | Out-Null
          Copy-Item $dll.FullName    (Join-Path $out "LiveSplit.SmwCounters.dll")
          Copy-Item "README.md"      $out
          Copy-Item "LICENSE"        $out
          Copy-Item "CREDITS.md"     $out
          $zip = "LiveSplit.SmwCounters-${{ steps.ver.outputs.version }}.zip"
          Compress-Archive -Path "$out/*" -DestinationPath $zip -Force
          "dll=$($dll.FullName)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
          "zip=$zip"             | Out-File -FilePath $env:GITHUB_OUTPUT -Append

      - name: Publish GitHub Release
        if: github.ref_type == 'tag'
        uses: softprops/action-gh-release@v2
        with:
          name: ${{ steps.ver.outputs.version }}
          files: |
            ${{ steps.stage.outputs.dll }}
            ${{ steps.stage.outputs.zip }}

      - name: Upload build artifacts (manual runs)
        if: github.ref_type != 'tag'
        uses: actions/upload-artifact@v4
        with:
          name: LiveSplit.SmwCounters-${{ steps.ver.outputs.version }}
          path: |
            ${{ steps.stage.outputs.dll }}
            ${{ steps.stage.outputs.zip }}
```

- [ ] **Step 2: Validate the workflow YAML parses**

Run: `pwsh -Command "python -c \"import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml')); print('ok')\""` (or any available YAML linter)
Expected: `ok`. If no YAML tool is available, visually confirm indentation and that every `uses:` pins a version.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "Add release workflow: tag or manual dispatch builds and publishes the DLL"
```

---

### Task 7: Cut the first release (manual, gated on prior tasks)

**Files:** none (git operations only).

**Interfaces:**
- Consumes: all prior tasks pushed to `origin`.
- Produces: the `v0.1.0` GitHub Release with assets — the deliverable of the whole plan.

- [ ] **Step 1: Push branch and open/merge to `master`** per the repo's normal flow (do not tag until the workflow file is on the default branch).

- [ ] **Step 2: Dry-run the workflow manually** from the GitHub Actions tab (`workflow_dispatch`) and confirm the uploaded artifact contains `LiveSplit.SmwCounters.dll` + README + LICENSE + CREDITS.

- [ ] **Step 3: Tag and push**

```bash
git tag v0.1.0
git push origin v0.1.0
```

- [ ] **Step 4: Verify the Release** appears at `https://github.com/amcknight/LiveSplit.SmwCounters/releases/latest` with both the loose `.dll` and the zip attached, and that downloading the DLL and dropping it into a real `LiveSplit/Components/` folder loads as **SMW Counters** in Edit Layout.

---

## Deferred (not in this plan)

- **Tier 2 — community listings:** add `livesplit-component`, `smw`, `speedrun`
  GitHub topics; create a speedrun.com / SMWCentral resource entry (do after
  `v0.1.0` exists).
- **Tier 3 — promotion:** SGDQ backrooms demo → recruit testers → SMWCentral
  thread (post-SGDQ).
- **Screenshot:** capture and drop into the README where the `TODO` marker is.
