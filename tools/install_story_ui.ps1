param([string]$Build)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install_helpers.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$Build) { $Build = Get-CampaignsBuildOutput $projectRoot 'Client' }
$paths = Get-CampaignsPaths
$expected = (Get-FileHash -LiteralPath (Join-Path $Build 'WTT-Campaigns.Shared.dll') -Algorithm SHA256).Hash
foreach ($directory in @($paths.CampaignsClientDir, $paths.CampaignsServerDir)) {
    if ((Get-FileHash -LiteralPath (Join-Path $directory 'WTT-Campaigns.Shared.dll') -Algorithm SHA256).Hash -ne $expected) { throw 'The installed shared contract differs from this validated build; install matching components with build.proj.' }
}
$files = foreach ($name in @('WTT-Campaigns.Client.dll', 'WTT-Campaigns.UI.dll')) {
    $source = Join-Path $Build $name
    @{ Source = $source; InstallPath = 'client/' + $name; Hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
}
Install-CampaignsFiles -Files $files
