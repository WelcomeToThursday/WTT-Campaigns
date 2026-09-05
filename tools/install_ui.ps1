param([string]$Package)
$ErrorActionPreference = 'Stop'
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
    $Package = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'release') -Directory -Filter "SeasonalPerks-UI-$version-*" | Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$Package) { throw 'Run package_ui.ps1 first.' }
$Package = [IO.Path]::GetFullPath($Package)
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
$backup = Join-Path $projectRoot ('Testing\UIBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($file in $files)
{
    if (Test-Path -LiteralPath $file.target)
    {
        $destination = Join-Path $backup $file.relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $file.target -Destination $destination
    }
}
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
Write-Output "Installed Seasonal Perks UI package. Previous mod files are backed up in $backup. Start the server, then the game."
