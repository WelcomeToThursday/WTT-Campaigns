param([string]$Package)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build_helpers.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
if (Get-Process EscapeFromTarkov -ErrorAction SilentlyContinue) { throw 'Close the game before installing the UI update.' }
$serverPath = Join-Path $sptRoot 'SPT_Runtime\SPT.Server.exe'
if (Get-Process SPT.Server -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $serverPath })
{
    throw 'Close the installed SPT server before installing the UI update. Its loaded mod assembly is locked.'
}
if (!$Package)
{
    $version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    $Package = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'release') -Directory -Filter "WTT-Seasonal-UI-$version-*" | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$Package) { throw 'Run package_ui.ps1 first.' }
$Package = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Package).TrimEnd('\', '/')
$manifest = Get-Content -LiteralPath (Join-Path $Package 'manifest.json') -Raw | ConvertFrom-Json
$files = @()
foreach ($entry in $manifest)
{
    if ($entry.path -notmatch '^(BepInEx|SPT_Runtime)[\\/]') { continue }
    $source = [IO.Path]::GetFullPath((Join-Path $Package $entry.path))
    $target = [IO.Path]::GetFullPath((Join-Path $sptRoot $entry.path))
    if (!$source.StartsWith($Package + '\', [StringComparison]::OrdinalIgnoreCase) -or !$target.StartsWith($sptRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Package checksum failed: $($entry.path)" }
    $files += @{ relative = $entry.path; source = $source; target = $target }
}
if (!$files.Count) { throw 'Package contains no installable files.' }
$packageFiles = @($files | ForEach-Object { $_.relative.Replace('/', '\') })
foreach ($required in @(
    'BepInEx\plugins\SeasonalPerks\WTT-Seasonal.Client.dll',
    'BepInEx\plugins\SeasonalPerks\WTT-Seasonal.UI.dll',
    'BepInEx\plugins\SeasonalPerks\WTT-Seasonal.Shared.dll',
    'BepInEx\plugins\SeasonalPerks\seasonalperks_ui.bundle',
    'SPT_Runtime\user\mods\SeasonalPerks\WTT-Seasonal.Server.dll',
    'SPT_Runtime\user\mods\SeasonalPerks\WTT-Seasonal.Shared.dll',
    'SPT_Runtime\user\mods\SeasonalPerks\WTT-Seasonal.Server.deps.json',
    'SPT_Runtime\user\mods\SeasonalPerks\Newtonsoft.Json.dll'
)) {
    if ($required -notin $packageFiles) { throw "Package is missing a required file: $required" }
}
$backup = Join-Path $projectRoot ('Testing\UIBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
foreach ($file in $files)
{
    if (Test-Path -LiteralPath $file.target)
    {
        $destination = Join-Path $backup $file.relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.target -Destination $destination
    }
}
Backup-LegacySeasonalAssemblies $sptRoot $backup @('BepInEx\plugins\SeasonalPerks', 'SPT_Runtime\user\mods\SeasonalPerks')
$obsoleteArtwork = [IO.Path]::GetFullPath((Join-Path $sptRoot 'SPT_Runtime\user\mods\SeasonalPerks\selection-artwork'))
$artworkBackup = [IO.Path]::GetFullPath((Join-Path $backup 'SPT_Runtime\user\mods\SeasonalPerks\selection-artwork'))
if (!$obsoleteArtwork.StartsWith($sptRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or !$artworkBackup.StartsWith($backup + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid artwork migration path.' }
if (Test-Path -LiteralPath $obsoleteArtwork)
{
    New-Item -ItemType Directory -Path (Split-Path $artworkBackup -Parent) -Force | Out-Null
    Move-Item -LiteralPath $obsoleteArtwork -Destination $artworkBackup
}
foreach ($file in $files)
{
    New-Item -ItemType Directory -Path (Split-Path $file.target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.source -Destination $file.target
}
foreach ($file in $files)
{
    if ((Get-FileHash -LiteralPath $file.source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.target -Algorithm SHA256).Hash)
    {
        throw "Installed checksum mismatch: $($file.relative). Previous files are backed up in $backup."
    }
}
Write-Output "Installed and verified $($files.Count) Seasonal Perks files. Previous mod files are backed up in $backup. Start the server, then the game."
