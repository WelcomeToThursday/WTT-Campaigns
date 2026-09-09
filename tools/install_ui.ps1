param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install_helpers.ps1')
Install-SeasonalPackage -Package $Package
