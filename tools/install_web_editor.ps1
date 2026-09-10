param(
    [Parameter(Mandatory)][string]$Package,
    [string]$SptRoot,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install_helpers.ps1')
$packageRoot = (Resolve-Path -LiteralPath $Package).Path
$paths = Get-CampaignsPaths -TarkovDir $SptRoot
$mod = $paths.CampaignsServerDir
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
$allowed = @('WTT-Campaigns.Server.dll', 'wwwroot/creator.css')
if (@($manifest.files).Count -ne $allowed.Count -or @($manifest.files.path | Sort-Object -Unique).Count -ne $allowed.Count) { throw 'Invalid web editor package.' }
if ((Get-FileHash -LiteralPath (Join-Path $mod 'WTT-Campaigns.Shared.dll') -Algorithm SHA256).Hash -ne $manifest.sharedSha256) { throw 'The installed shared assembly differs from the validated package.' }
$files = foreach ($entry in $manifest.files) {
    if ($entry.path -notin $allowed -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid web editor package entry.' }
    @{ Source = (Join-Path $packageRoot $entry.path); InstallPath = 'server/' + $entry.path; Hash = $entry.sha256 }
}
Install-CampaignsFiles -Files $files -CheckOnly:$CheckOnly -TarkovDir $SptRoot
