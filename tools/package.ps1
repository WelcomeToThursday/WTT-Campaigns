$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build_helpers.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$bundle = Join-Path $projectRoot 'Client\Resources\seasonalperks_ui.bundle'
if (!(Test-Path -LiteralPath $bundle)) { throw 'Build the recovered UI in CJ-SDK first.' }

dotnet build (Join-Path $projectRoot 'WTT-Seasonal.slnx') -c Release --nologo -v:q | Out-Host
if ($LASTEXITCODE) { throw 'Release build failed.' }
$clientOutput = Get-SeasonalBuildOutput $projectRoot 'Client'
$serverOutput = Get-SeasonalBuildOutput $projectRoot 'Server'
dotnet run --project (Join-Path $projectRoot 'Tests') -c Release --no-build -- (Join-Path $sptRoot 'BepInEx\DumpedAssemblies\EscapeFromTarkov\Assembly-CSharp.dll') | Out-Host
if ($LASTEXITCODE) { throw 'Contract or client compatibility checks failed.' }
dotnet run --project (Join-Path $projectRoot 'Tests') -c Release --no-build -- --resource-hooks $sptRoot (Join-Path $clientOutput 'WTT-Seasonal.Client.dll') | Out-Host
if ($LASTEXITCODE) { throw 'Item-resource client hook checks failed.' }

dotnet run --project (Join-Path $projectRoot 'Tests') -c Release --no-build -- --bush-hooks $sptRoot (Join-Path $clientOutput 'WTT-Seasonal.Client.dll') | Out-Host
if ($LASTEXITCODE) { throw 'Bush client hook checks failed.' }

dotnet run --project (Join-Path $projectRoot 'Tests') -c Release --no-build -- --experience-hooks $sptRoot (Join-Path $clientOutput 'WTT-Seasonal.Client.dll') | Out-Host
if ($LASTEXITCODE) { throw 'Experience client hook checks failed.' }

# Unique staging folders preserve previous builds and avoid destructive cleanup.
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$output = Join-Path $projectRoot ('release\WTT-Seasonal-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$client = Join-Path $output 'BepInEx\plugins\SeasonalPerks'
$server = Join-Path $output 'SPT_Runtime\user\mods\SeasonalPerks'
New-Item -ItemType Directory -Path $client, $server -Force | Out-Null
foreach ($name in @('WTT-Seasonal.Client.dll', 'WTT-Seasonal.UI.dll', 'WTT-Seasonal.Shared.dll', 'seasonalperks_ui.bundle'))
{
    Copy-Item -LiteralPath (Join-Path $clientOutput $name) -Destination $client
}
Copy-SeasonalServerOutput $serverOutput $server
foreach ($name in @('README.md', 'CONTRIBUTING.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $output -Recurse
$manifest = Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($output, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
Write-Host "Staged build: $output"
Write-Output $output
