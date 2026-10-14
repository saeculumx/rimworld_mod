param(
    [string]$RimWorldDir = 'D:\SteamLibrary\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'D:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$sdkExe = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdkExe)) { $sdkExe = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $taskRoot '.tools\nuget'
$env:APPDATA = Join-Path $taskRoot '.tools\appdata'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$config = Join-Path $taskRoot 'NuGet.Config'
$project = Join-Path $taskRoot 'Source\LocalRail\LocalRail.csproj'
# No PackageReference: restore is offline and all game references remain in their installation.
& $sdkExe restore $project --configfile $config --ignore-failed-sources "-p:RimWorldDir=$RimWorldDir" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $sdkExe build $project -c Release --no-restore "-p:RimWorldDir=$RimWorldDir" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$assemblies = Join-Path $taskRoot 'Mod\LocalRail\Assemblies'
New-Item -ItemType Directory -Path $assemblies -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'Source\LocalRail\bin\Release\net10.0\LocalRail.dll') -Destination $assemblies -Force
if ($Test) {
    $tests = Join-Path $taskRoot 'Tests\LocalRail.Tests.csproj'
    & $sdkExe restore $tests --configfile $config --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
    & $sdkExe run --project $tests -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Graph/cost verification failed.' }
}
$outputDir = Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
[xml]$modAbout = Get-Content -LiteralPath (Join-Path $taskRoot 'Mod\LocalRail\About\About.xml') -Raw
$packagePath = Join-Path $outputDir "LocalRail-$($modAbout.ModMetaData.modVersion)-rw1.7-poc.zip"
Compress-Archive -Path (Join-Path $taskRoot 'Mod\LocalRail') -DestinationPath $packagePath -Force
Write-Output "Built and packaged: $packagePath"

