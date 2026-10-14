param([string]$RimWorldDir = 'D:\SteamLibrary\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Mod\LocalRail'
if (!(Test-Path -LiteralPath (Join-Path $source 'Assemblies\LocalRail.dll'))) { throw 'Build the mod first with build.ps1.' }
if (!(Test-Path -LiteralPath (Join-Path $RimWorldDir 'Assembly-CSharp.dll'))) { throw 'Invalid RimWorld 1.7 installation.' }
$target = Join-Path $RimWorldDir 'Mods\LocalRail'
if (Test-Path -LiteralPath $target) { throw "Install target already exists: $target. Inspect it before updating; this script does not overwrite existing mods." }
Copy-Item -LiteralPath $source -Destination $target -Recurse
Write-Output "Installed $target. Enable Harmony and Local Rail in the game's Mods menu."
