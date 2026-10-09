<#
.SYNOPSIS
  Build, re-import the shaders and run a hidden selftest (via ..\_Tools\godot-run.ps1).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\test.ps1 -Name base -Seconds 30 -Extra "--top"
#>
param(
    [string]$Name = "test",
    [int]$Seconds = 30,
    [string]$Extra = "",
    [switch]$NoBuild
)
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$wt = Split-Path -Leaf $root
if (-not $NoBuild) {
    $b = dotnet build 2>&1 | Select-String -Pattern " error |Build succeeded"
    $b | ForEach-Object { Write-Output $_.Line }
}
# Always re-import (also with -NoBuild): the game loads the imported SPIR-V, an edited .glsl is not seen without it.
& "C:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64_console.exe" --headless --path . --import 2>&1 | Out-Null
$shot = Join-Path $root "out\$Name.png"
$timeout = $Seconds + 40
powershell -NoProfile -ExecutionPolicy Bypass -File ..\_Tools\godot-run.ps1 -Title "River-${wt}: $Name" -Timeout $timeout `
    -GodotArgs "--path . --resolution 1600x900 -- --selftest $Seconds --shot $shot $($Seconds - 1) $Extra"
