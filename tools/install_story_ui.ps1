param([string]$Build)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install_helpers.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$Build) { $Build = Get-SeasonalBuildOutput $projectRoot 'Client' }
$paths = Get-SeasonalPaths
$expected = (Get-FileHash -LiteralPath (Join-Path $Build 'WTT-Seasonal.Shared.dll') -Algorithm SHA256).Hash
foreach ($directory in @($paths.SeasonalClientDir, $paths.SeasonalServerDir)) {
    if ((Get-FileHash -LiteralPath (Join-Path $directory 'WTT-Seasonal.Shared.dll') -Algorithm SHA256).Hash -ne $expected) { throw 'The installed shared contract differs from this validated build; install matching components with build.proj.' }
}
$files = foreach ($name in @('WTT-Seasonal.Client.dll', 'WTT-Seasonal.UI.dll')) {
    $source = Join-Path $Build $name
    @{ Source = $source; InstallPath = 'client/' + $name; Hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash }
}
Install-SeasonalFiles -Files $files
