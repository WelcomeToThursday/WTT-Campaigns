param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'install_helpers.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
$version = (Get-SeasonalPaths).Version
$output = Join-Path $projectRoot ('release/WTT-Seasonal-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
& dotnet msbuild (Join-Path $projectRoot 'build.proj') -t:Package "-p:Configuration=$Configuration" "-p:PackageDir=$output" -nologo -v:minimal | Out-Host
if ($LASTEXITCODE) { throw "Build/package/install failed. Outputs remain in the build folders and, if staged, $output. No servers or clients were stopped or started." }
$manifest = Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($output, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
Write-Host "Installed and packaged build: $output"
Write-Output $output
