<#
.SYNOPSIS
Renders the SMW Counters settings control offscreen, the way LiveSplit's
Layout Settings dialog would show it on this machine, and dumps every
control's bounds. Lets you "see" the dialog without launching LiveSplit.

.DESCRIPTION
Why this exists (2026-10-25): LiveSplit calls SetProcessDPIAware, so inside
it control fonts follow the display DPI while any fixed pixel layout does
not, and it hands the settings control its own dialog font (Segoe UI 9) when
it parents it, which re-measures every auto-sized button and label. Both
effects are invisible at 100% scaling and in a plain test process. This
script reproduces them: it makes itself DPI-aware, enables visual styles,
builds the control the way the component does, then applies the host font.

The recipe is generic for any LiveSplit component settings control; only the
`$rows` block below (what SmwCountersComponent passes to BuildUi) is specific
to this plugin. Copy the script and replace that block for another plugin.

.PARAMETER Dll
Path to the built SMWCounters.dll (default: the Release artifact).

.PARAMETER Out
PNG path to write (default: settings-render.png next to this script).

.PARAMETER HostFont
Font name and point size the host dialog will impose. LiveSplit's dialogs
use Segoe UI 9 on Windows 10/11. Pass '' to keep the WinForms default.

.EXAMPLE
dotnet build src/SMWCounters/SMWCounters.csproj -c Release
pwsh scripts/render-settings.ps1
# then open scripts/settings-render.png

.NOTES
Offscreen rendering (Control.DrawToBitmap) does not paint TextBox contents;
their bounds in the geometry dump are still right. Reads the DPI from the
screen DC, so run it on the display scaling you want to check.
#>
param(
  [string]$Dll = (Join-Path $PSScriptRoot '..\artifacts\bin\SMWCounters\release\SMWCounters.dll'),
  [string]$Out = (Join-Path $PSScriptRoot 'settings-render.png'),
  [string]$HostFont = 'Segoe UI,9'
)

Add-Type -TypeDefinition @"
using System.Runtime.InteropServices;
public static class DpiNative { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[void][DpiNative]::SetProcessDPIAware()
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
foreach ($dep in 'UpdateManager.dll', 'LiveSplit.Core.dll', 'SNES.dll') {
  [void][Reflection.Assembly]::LoadFrom((Join-Path $root "lib\$dep"))
}
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $Dll).Path)
$settingsType = $asm.GetType('LiveSplit.UI.Components.SmwCountersComponentSettings')
$settings = [Activator]::CreateInstance($settingsType, @($false))   # allowGamepads: false

# --- plugin-specific: the rows SmwCountersComponent passes to BuildUi -------
# (Id, DefaultLabel, HasBankToggle, Extras, ResetValue, GetValue, SetValue, RefreshExtras)
$create = [System.ValueTuple].GetMethods() |
  Where-Object { $_.Name -eq 'Create' -and $_.GetGenericArguments().Count -eq 8 } | Select-Object -First 1
$create = $create.MakeGenericMethod(@([string], [string], [bool], [System.Windows.Forms.Control],
  [System.Action], [System.Func[int]], [System.Action[int]], [System.Action]))
$noop = [System.Action]{ }; $zero = [System.Func[int]]{ 0 }; $set = [System.Action[int]]{ param($v) }
$counters = @(
  @('deaths', 'Deaths', $false), @('exits', 'Exits', $false), @('moons', 'Moons', $true),
  @('jumps', 'Jumps', $true), @('powerups', 'Powerups', $true), @('coins', 'Coins', $true),
  @('dragoncoins', 'Dragon Coins', $false), @('kills', 'Kills', $true), @('oneups', '1-Ups', $true))
$tupleType = $create.Invoke($null, @('x', 'x', $true, $null, $noop, $zero, $set, $null)).GetType()
$rows = [Array]::CreateInstance($tupleType, $counters.Count)
for ($i = 0; $i -lt $counters.Count; $i++) {
  $c = $counters[$i]
  # The Kills row's radio panel is omitted: a PowerShell-wrapped Panel cannot
  # pass through MethodInfo.Invoke. Its placement follows the same rule as
  # the bank checkbox, so the checkbox column stands in for it here.
  $rows[$i] = $create.Invoke($null, @($c[0], $c[1], $c[2], $null, $noop, $zero, $set, $null))
}
# ---------------------------------------------------------------------------

try { $settings.BuildUi($rows) }
catch {
  $ex = $_.Exception; while ($ex.InnerException) { $ex = $ex.InnerException }
  Write-Error "BuildUi failed: $($ex.GetType().Name): $($ex.Message)"; exit 1
}
if ($HostFont) {
  $parts = $HostFont.Split(','); $settings.Font = New-Object System.Drawing.Font($parts[0].Trim(), [single]$parts[1])
}
$settings.CreateControl()
$settings.PerformLayout()

$w = $settings.Width; $h = $settings.Height
$dpi = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero).DpiX
"control {0}x{1} at {2} DPI ({3:P0}); font {4} {5}pt" -f $w, $h, $dpi, ($dpi / 96), $settings.Font.Name, $settings.Font.Size
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$settings.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $w, $h)))
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $Out"

# Geometry dump in z-order (index 0 = front). Overlaps show up as a later
# control's x inside an earlier control's [x, right).
$i = 0
foreach ($ctl in $settings.Controls) {
  $txt = $ctl.Text; if ($txt.Length -gt 28) { $txt = $txt.Substring(0, 28) + '...' }
  "{0,2} {1,-13} x={2,3} right={3,3} y={4,3} h={5,2} '{6}'" -f $i, $ctl.GetType().Name, $ctl.Left, $ctl.Right, $ctl.Top, $ctl.Height, $txt
  $i++
}
