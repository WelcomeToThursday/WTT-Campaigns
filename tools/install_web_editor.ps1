param(
    [Parameter(Mandatory)][string]$Package,
    [string]$SptRoot,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install_helpers.ps1')
$packageRoot = (Resolve-Path -LiteralPath $Package).Path
$paths = Get-SeasonalPaths -TarkovDir $SptRoot
$mod = $paths.SeasonalServerDir
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
$allowed = @('WTT-Seasonal.Server.dll', 'wwwroot/creator.css')
if (@($manifest.files).Count -ne $allowed.Count -or @($manifest.files.path | Sort-Object -Unique).Count -ne $allowed.Count) { throw 'Invalid web editor package.' }
if ((Get-FileHash -LiteralPath (Join-Path $mod 'WTT-Seasonal.Shared.dll') -Algorithm SHA256).Hash -ne $manifest.sharedSha256) { throw 'The installed shared assembly differs from the validated package.' }
$files = foreach ($entry in $manifest.files) {
    if ($entry.path -notin $allowed -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid web editor package entry.' }
    @{ Source = (Join-Path $packageRoot $entry.path); InstallPath = 'server/' + $entry.path; Hash = $entry.sha256 }
}
Install-SeasonalFiles -Files $files -CheckOnly:$CheckOnly -TarkovDir $SptRoot
