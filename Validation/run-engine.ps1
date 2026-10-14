param(
    [string]$RimWorldDir = 'D:\SteamLibrary\steamapps\common\RimWorld',
    [switch]$LoadSavedTrip,
    [switch]$LegacyLoad,
    [switch]$PreviousLoad,
    [switch]$Visual,
    [switch]$Junctions,
    [switch]$Network,
    [switch]$NetworkLoad,
    [switch]$UpgradeNetwork,
    [switch]$Warehouse,
    [switch]$Retrofit,
    [switch]$Demand,
    [switch]$DemandLoad,
    [switch]$Operations,
    [switch]$OperationsLoad,
    [switch]$WarehouseLoad,
    [switch]$UpgradePhysical,
    [switch]$LegacyPlans,
    [switch]$Physical,
    [switch]$PhysicalLoad
)
$ErrorActionPreference = 'Stop'
if($UpgradeNetwork){$NetworkLoad=$true}
if($UpgradePhysical){$PhysicalLoad=$true}
if($LegacyPlans){$WarehouseLoad=$true}
$taskRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $taskRoot 'Mod\LocalRail'
$target = Join-Path $RimWorldDir 'Mods\LocalRail'
if (!(Test-Path -LiteralPath (Join-Path $source 'Assemblies\LocalRail.dll'))) { throw 'Run build.ps1 first.' }
if (Test-Path -LiteralPath $target) {
    [xml]$installedAbout = Get-Content -LiteralPath (Join-Path $target 'About\About.xml') -Raw
    if ($installedAbout.ModMetaData.packageId -ne 'localrail.automatic.poc') { throw 'Refusing to overwrite an unrelated mod.' }
    Get-ChildItem -LiteralPath $source | Copy-Item -Destination $target -Recurse -Force
} else { Copy-Item -LiteralPath $source -Destination $target -Recurse }
$profile = Join-Path $taskRoot $(if($Demand -or $DemandLoad){'.local\LocalRailTestDemand'}elseif($Operations -or $OperationsLoad){'.local\LocalRailTestOperations'}elseif($Retrofit){'.local\LocalRailTestRetrofit'}elseif($Warehouse -or $WarehouseLoad){'.local\LocalRailTestWarehouse'}elseif($UpgradePhysical){'.local\LocalRailTestPhysicalUpgrade'}elseif($Physical -or $PhysicalLoad){'.local\LocalRailTestPhysical'}elseif($UpgradeNetwork){'.local\LocalRailTestNetworkUpgrade'}elseif($Network -or $NetworkLoad){'.local\LocalRailTestNetwork'}elseif($Junctions){'.local\LocalRailTestJunctions'}else{'.local\LocalRailTest'})
$configDir = Join-Path $profile 'Config'
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
# Listing known DLCs prevents RimWorld from auto-enabling newly discovered expansions.
$mods = @'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
<version>1.7.4977 rev1248</version>
<activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>localrail.automatic.poc</li></activeMods>
<knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions>
</ModsConfigData>
'@
Set-Content -LiteralPath (Join-Path $configDir 'ModsConfig.xml') -Value $mods -Encoding utf8
$prefs = @'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData><devMode>True</devMode><langFolderName>English</langFolderName><runInBackground>True</runInBackground><pauseOnLoad>False</pauseOnLoad><pauseOnError>False</pauseOnError><volumeMaster>0</volumeMaster><fullscreen>False</fullscreen><screenWidth>1280</screenWidth><screenHeight>720</screenHeight></PrefsData>
'@
Set-Content -LiteralPath (Join-Path $configDir 'Prefs.xml') -Value $prefs -Encoding utf8
if ($LegacyLoad) {
    $saves = Join-Path $profile 'Saves'
    New-Item -ItemType Directory -Path $saves -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot '.local\baseline-0.2\LocalRailSmokeInTransit.rws') -Destination (Join-Path $saves 'LocalRailSmokeLegacy.rws') -Force
}
if ($PreviousLoad) {
    $saves = Join-Path $profile 'Saves'
    New-Item -ItemType Directory -Path $saves -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot '.local\baseline-0.3\LocalRailSmokeInTransit.rws') -Destination (Join-Path $saves 'LocalRailSmokePrevious.rws') -Force
}
if($UpgradeNetwork){
    $saves=Join-Path $profile 'Saves'
    New-Item -ItemType Directory -Path $saves -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot '.local\baseline-0.5\LocalRailNetworkInTransit.rws') -Destination (Join-Path $saves 'LocalRailNetworkInTransit.rws') -Force
}
if($UpgradePhysical){
    $saves=Join-Path $profile 'Saves'
    New-Item -ItemType Directory -Path $saves -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot '.local\baseline-0.6\LocalRailPhysicalGate.rws') -Destination (Join-Path $saves 'LocalRailPhysicalGate.rws') -Force
}
if($LegacyPlans){
    $railPlanSave=Join-Path $profile 'Saves\LocalRailWarehouseInTransit.rws'
    [xml]$railPlanXml=Get-Content -LiteralPath $railPlanSave -Raw
    $railPlanNodes=@($railPlanXml.SelectNodes('//*[def]') | Where-Object {$_.def -match 'LR_RailDoor' -and $_.GetAttribute('Class') -match 'Blueprint|Frame'})
    if($railPlanNodes.Count-ne2){throw 'Expected two isolated unfinished rail door plans in the warehouse fixture.'}
    $railRemoved=0
    foreach($railStuffNode in @($railPlanXml.SelectNodes('//stuff|//stuffToUse'))){
        if($railStuffNode.ParentNode.def -match 'LR_RailDoor' -and $railStuffNode.ParentNode.GetAttribute('Class') -match 'Blueprint|Frame'){
            [void]$railStuffNode.ParentNode.RemoveChild($railStuffNode);$railRemoved++
        }
    }
    if($railRemoved-ne2-and$railRemoved-ne0){throw "Expected two material entries or an already converted fixture, found $railRemoved."}
    $railPlanXml.Save($railPlanSave)
}
$suffix = if($DemandLoad){'demand-load'}elseif($Demand){if($Visual){'demand-visual'}else{'demand'}}elseif($OperationsLoad){'operations-load'}elseif($Operations){if($Visual){'operations-visual'}else{'operations'}}elseif($Retrofit){if($Visual){'retrofit-visual'}else{'retrofit'}}elseif($LegacyPlans){'warehouse-legacy-plans'}elseif($WarehouseLoad){'warehouse-load'}elseif($Warehouse){if($Visual){'warehouse-visual'}else{'warehouse'}}elseif($UpgradePhysical){'upgrade-0.6'}elseif($PhysicalLoad){'physical-load'}elseif($Physical){if($Visual){'physical-visual'}else{'physical'}}elseif($UpgradeNetwork){'upgrade-0.5'}elseif($NetworkLoad){'network-load'}elseif($Network){if($Visual){'network-visual'}else{'network-trip'}}elseif ($Junctions) {'junctions'} elseif ($LegacyLoad) { 'legacy' } elseif ($PreviousLoad) { 'previous' } elseif ($LoadSavedTrip) { 'load' } elseif ($Visual) { 'visual' } else { 'trip' }
$switch = if($DemandLoad){'-localrail-demand-load-smoke'}elseif($Demand){'-localrail-demand-smoke'}elseif($OperationsLoad){'-localrail-operations-load-smoke'}elseif($Operations){'-localrail-operations-smoke'}elseif($Retrofit){'-localrail-retrofit-smoke'}elseif($WarehouseLoad){'-localrail-warehouse-load-smoke'}elseif($Warehouse){'-localrail-warehouse-smoke'}elseif($PhysicalLoad){'-localrail-physical-load-smoke'}elseif($Physical){'-localrail-physical-smoke'}elseif($NetworkLoad){'-localrail-network-load-smoke'}elseif($Network){'-localrail-network-smoke'}elseif ($Junctions) {'-localrail-junction-smoke'} elseif ($LegacyLoad) { '-localrail-legacy-load-smoke' } elseif ($PreviousLoad) { '-localrail-previous-load-smoke' } elseif ($LoadSavedTrip) { '-localrail-load-smoke' } else { '-localrail-smoke' }
$stdout = Join-Path $taskRoot ".local\engine-$suffix.log"
$stderr = Join-Path $taskRoot ".local\engine-$suffix-stderr.log"
$result = Join-Path $profile 'smoke-result.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$gameArgs = @('-NoFileLog','-NoCrashDialog','-DisableLogStackTraces',"-savedatafolder=$profile",$switch)
if ($Visual -or $Junctions) { $gameArgs += '-localrail-visual-smoke' } else { $gameArgs += '-Headless' }
$testProcess = Start-Process -FilePath (Join-Path $RimWorldDir 'RimWorld.exe') -WorkingDirectory $RimWorldDir `
    -ArgumentList $gameArgs `
    -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
Write-Output "Started isolated engine test: PID $($testProcess.Id), profile $profile"
$timeoutAt = (Get-Date).AddSeconds(180)
while (!$testProcess.WaitForExit(1000)) {
    if ((Get-Date) -gt $timeoutAt) { $testProcess.Kill(); throw "Own test process timed out. Inspect $stdout" }
}
if (!(Test-Path -LiteralPath $result)) { Get-Content -LiteralPath $stdout -Tail 30; throw 'Engine test did not produce a result.' }
Copy-Item -LiteralPath $result -Destination (Join-Path $PSScriptRoot "engine-$suffix-result.json") -Force
Get-Content -LiteralPath $result
[pscustomobject]$verdict = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
if (!$verdict.completed) { throw "Engine test failed. Inspect $stdout" }
$errors = Select-String -LiteralPath $stdout -Pattern '\bERROR\b'
if ($errors) { $errors | ForEach-Object { Write-Output $_.Line }; throw 'Engine emitted errors despite functional test result.' }
Copy-Item -LiteralPath $stdout -Destination (Join-Path $PSScriptRoot "engine-$suffix.log") -Force
if($Demand -and $Visual){
    foreach($name in @('rail-travel-diagnostic.png','rail-empty-automatic-cargo.png','rail-train-types.png')){Copy-Item -LiteralPath (Join-Path $profile $name) -Destination (Join-Path $PSScriptRoot $name) -Force}
} elseif($Operations -and $Visual){
    foreach($name in @('rail-visible-cargo.png','rail-rotated-stations.png')){Copy-Item -LiteralPath (Join-Path $profile $name) -Destination (Join-Path $PSScriptRoot $name) -Force}
} elseif($Retrofit -and $Visual){
    foreach($name in @('rail-industrial-gates.png','rail-platform-picker.png','rail-cargo-slots.png')){Copy-Item -LiteralPath (Join-Path $profile $name) -Destination (Join-Path $PSScriptRoot $name) -Force}
} elseif($Warehouse -and $Visual){
    foreach($name in @('rail-warehouse-network.png','rail-warehouse-rules.png')){Copy-Item -LiteralPath (Join-Path $profile $name) -Destination (Join-Path $PSScriptRoot $name) -Force}
} elseif($Physical -and $Visual) {
    foreach($name in @('rail-physical-train.png','rail-terrain-door.png')) {
        Copy-Item -LiteralPath (Join-Path $profile $name) -Destination (Join-Path $PSScriptRoot $name) -Force
    }
} elseif($Network -and $Visual) {
    foreach($name in @('rail-network-manager.png','rail-platform-manager.png','rail-dwell-editor.png','rail-loop-editor.png')) {
        Copy-Item -LiteralPath (Join-Path $profile $name) -Destination (Join-Path $PSScriptRoot $name) -Force
    }
} elseif ($Junctions) {
    Copy-Item -LiteralPath (Join-Path $profile 'rail-junctions.png') -Destination (Join-Path $PSScriptRoot 'rail-junctions.png') -Force
} elseif ($Visual) {
    foreach ($screenshotName in @('rail-poc.png','rail-route-editor.png','rail-diagonal.png','rail-junctions.png')) {
        Copy-Item -LiteralPath (Join-Path $profile $screenshotName) -Destination (Join-Path $PSScriptRoot $screenshotName) -Force
    }
}
Write-Output "PASS engine test: $suffix"



