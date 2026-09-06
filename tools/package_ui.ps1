$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build_helpers.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$bundle = Join-Path $projectRoot 'Client\Resources\seasonalperks_ui.bundle'
if (!(Test-Path -LiteralPath $bundle)) { throw 'Build the recovered UI in CJ-SDK first.' }
dotnet build (Join-Path $projectRoot 'Client') -c Release --nologo -v:q | Out-Host
if ($LASTEXITCODE) { throw 'Client build failed.' }
dotnet build (Join-Path $projectRoot 'Server') -c Release --nologo -v:q | Out-Host
if ($LASTEXITCODE) { throw 'UI server adapter build failed.' }
$clientOutput = Get-SeasonalBuildOutput $projectRoot 'Client'
$serverOutput = Get-SeasonalBuildOutput $projectRoot 'Server'
dotnet run --project (Join-Path $projectRoot 'Tests') -c Release -- --ui (Join-Path $sptRoot 'BepInEx\DumpedAssemblies\EscapeFromTarkov\Assembly-CSharp.dll') (Join-Path $clientOutput 'WTT-Seasonal.Client.dll') | Out-Host
if ($LASTEXITCODE) { throw 'UI compatibility checks failed.' }
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$output = Join-Path $projectRoot ('release\WTT-Seasonal-UI-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$client = Join-Path $output 'BepInEx\plugins\SeasonalPerks'
New-Item -ItemType Directory -Path $client -Force | Out-Null
$server = Join-Path $output 'SPT_Runtime\user\mods\SeasonalPerks'
New-Item -ItemType Directory -Path $server -Force | Out-Null
Copy-SeasonalServerOutput $serverOutput $server
foreach ($name in @('WTT-Seasonal.Client.dll', 'WTT-Seasonal.UI.dll', 'WTT-Seasonal.Shared.dll', 'seasonalperks_ui.bundle'))
{
    Copy-Item -LiteralPath (Join-Path $clientOutput $name) -Destination $client
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\ui.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\creation-reference.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\skills-reference.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\battle-pass-ui.md') -Destination $output
foreach ($name in @('LICENSE', 'THIRD_PARTY_NOTICES.md'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output
}
$manifest = Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($output, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
Write-Host "Staged UI update: $output"
Write-Output $output
