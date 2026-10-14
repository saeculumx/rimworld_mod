param(
    [string]$RimWorldDir='D:\SteamLibrary\steamapps\common\RimWorld',
    [switch]$Physical,[switch]$PhysicalLoad,[switch]$Visual,[switch]$Network,[switch]$NetworkLoad,[switch]$UpgradeNetwork,[switch]$Warehouse,[switch]$WarehouseLoad,[switch]$UpgradePhysical,[switch]$LegacyPlans,
    [switch]$LoadSavedTrip,[switch]$PreviousLoad,[switch]$LegacyLoad,[switch]$Junctions
)
$ErrorActionPreference='Stop'
# An already cached official dependency is temporarily discoverable without Steam.
# The directory must be new and explicitly owned by this validation wrapper.
$railModsRoot=(Resolve-Path -LiteralPath (Join-Path $RimWorldDir 'Mods')).Path
$railTemporary=Join-Path $railModsRoot 'LocalRailValidationHarmony'
$railCached='D:\SteamLibrary\steamapps\workshop\content\294100\2009463077'
if(Test-Path -LiteralPath $railTemporary){throw 'Validation dependency directory already exists; refusing to overwrite it.'}
if(!(Test-Path -LiteralPath (Join-Path $railCached '1.7\Assemblies\0Harmony.dll'))){throw 'Cached official Harmony 1.7 is unavailable.'}
$railOwner='LocalRail validation dependency only; owned by run-offline-engine.ps1, thread 01a13607-7351-7751-aea7-8143221155b0'
New-Item -ItemType Directory -Path $railTemporary | Out-Null
Set-Content -LiteralPath (Join-Path $railTemporary 'localrail-validation-owned.txt') -Value $railOwner
try {
    foreach($railPart in @('About','LoadFolders.xml','1.7')){Copy-Item -LiteralPath (Join-Path $railCached $railPart) -Destination $railTemporary -Recurse}
    $railParameters=@{};foreach($railPair in $PSBoundParameters.GetEnumerator()){$railParameters[$railPair.Key]=$railPair.Value}
    & (Join-Path $PSScriptRoot 'run-engine.ps1') @railParameters
} finally {
    $railResolved=(Resolve-Path -LiteralPath $railTemporary).Path
    if($railResolved-ne(Join-Path $railModsRoot 'LocalRailValidationHarmony') -or
       !$railResolved.StartsWith($railModsRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Validation dependency cleanup escaped the intended Mods directory.'
    }
    if((Get-Content -LiteralPath (Join-Path $railResolved 'localrail-validation-owned.txt') -Raw).Trim()-ne$railOwner){throw 'Validation dependency ownership changed; refusing cleanup.'}
    Remove-Item -LiteralPath $railResolved -Recurse -Force
}
