#requires -Version 5
<#
    Downloads a pinned LiveSplit release and extracts LiveSplit.Core.dll (and its
    compile-time sibling dependencies) into lib/.
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

# LiveSplit.Core.dll's public API surfaces types from these sibling assemblies at
# compile time (e.g. IUpdateable from UpdateManager.dll via IComponent.Update), so
# they must be fetched alongside it for the standalone/CI Reference-based build.
$assemblies = @("LiveSplit.Core.dll", "UpdateManager.dll")

New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$url     = "https://github.com/LiveSplit/LiveSplit/releases/download/$Version/LiveSplit_$Version.zip"
$tmpZip  = Join-Path ([System.IO.Path]::GetTempPath()) "LiveSplit_$Version.zip"
$tmpDir  = Join-Path ([System.IO.Path]::GetTempPath()) "LiveSplit_$Version"

Write-Host "Downloading $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $tmpZip

if (Test-Path $tmpDir) { Remove-Item -Recurse -Force $tmpDir }
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force

foreach ($name in $assemblies) {
    $found = Get-ChildItem -Path $tmpDir -Filter $name -Recurse | Select-Object -First 1
    if (-not $found) { throw "$name not found in $url" }

    $dest = Join-Path $libDir $name
    Copy-Item -Path $found.FullName -Destination $dest -Force
    Write-Host "Wrote $dest"
}
