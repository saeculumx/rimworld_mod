param([string]$RimWorldDir = 'D:\SteamLibrary\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $taskRoot 'Mod\LocalRail'
$installed = Join-Path $RimWorldDir 'Mods\LocalRail'
$xmlFiles = @(Get-ChildItem -LiteralPath $source -Recurse -Filter *.xml)
foreach ($file in $xmlFiles) { [xml]$document = Get-Content -LiteralPath $file.FullName -Raw }
[xml]$english = Get-Content -LiteralPath (Join-Path $source 'Languages\English\Keyed\LocalRail.xml') -Raw
[xml]$chinese = Get-Content -LiteralPath (Join-Path $source 'Languages\ChineseSimplified\Keyed\LocalRail.xml') -Raw
$englishKeys = @($english.LanguageData.ChildNodes | Where-Object NodeType -eq Element | ForEach-Object Name)
$chineseKeys = @($chinese.LanguageData.ChildNodes | Where-Object NodeType -eq Element | ForEach-Object Name)
if (Compare-Object $englishKeys $chineseKeys) { throw 'Language keys differ.' }
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($source,$file.FullName)
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $installed $relative)).Hash) {
        throw "Installed file differs: $relative"
    }
}
[xml]$modAbout = Get-Content -LiteralPath (Join-Path $source 'About\About.xml') -Raw
$version = $modAbout.ModMetaData.modVersion
$zipPath = Join-Path $taskRoot "dist\LocalRail-$version-rw1.7-poc.zip"
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $expected = @(Get-ChildItem -LiteralPath $source -Recurse -File)
    $entries = @($zip.Entries | Where-Object Name)
    if ($entries.Count -ne $expected.Count) { throw 'ZIP file count differs.' }
    foreach ($file in $expected) {
        $name = 'LocalRail/' + [IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/')
        $entry = $zip.GetEntry($name)
        if (!$entry) { throw "ZIP entry missing: $name" }
        $stream = $entry.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "ZIP entry differs: $name" }
    }
} finally { $zip.Dispose() }
$manifest = [ordered]@{
    version = $version
    game = (Get-Content -LiteralPath (Join-Path $RimWorldDir 'Version.txt') -Raw).Trim()
    dllSHA256 = (Get-FileHash -LiteralPath (Join-Path $source 'Assemblies\LocalRail.dll')).Hash
    installedAllFilesMatch = $true
    zipAllFilesMatch = $true
    zipSHA256 = (Get-FileHash -LiteralPath $zipPath).Hash
    zipBytes = (Get-Item -LiteralPath $zipPath).Length
    packageFileCount = $expected.Count
    xmlFilesParsed = $xmlFiles.Count
    bilingualKeyCount = $englishKeys.Count
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'package-manifest.json') -Encoding utf8
$manifest | ConvertTo-Json
