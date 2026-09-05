$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
dotnet build (Join-Path $projectRoot 'Client') -c Release --nologo -v:q
if ($LASTEXITCODE) { throw 'Client build failed.' }
dotnet build (Join-Path $projectRoot 'Server') -c Release --nologo -v:q
if ($LASTEXITCODE) { throw 'UI server adapter build failed.' }
dotnet run --project (Join-Path $projectRoot 'Tests') -c Release -- --ui (Join-Path $sptRoot 'BepInEx\DumpedAssemblies\EscapeFromTarkov\Assembly-CSharp.dll') (Join-Path $projectRoot 'Client\bin\Release\netstandard2.1\SeasonalPerks.Client.dll')
if ($LASTEXITCODE) { throw 'UI compatibility checks failed.' }
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$output = Join-Path $projectRoot ('release\SeasonalPerks-UI-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$client = Join-Path $output 'BepInEx\plugins\SeasonalPerks'
New-Item -ItemType Directory -Path $client -Force | Out-Null
$server = Join-Path $output 'SPT_Runtime\user\mods\SeasonalPerks'
New-Item -ItemType Directory -Path $server -Force | Out-Null
foreach ($name in @('SeasonalPerks.Server.dll', 'SeasonalPerks.Shared.dll', 'Newtonsoft.Json.dll', 'SeasonalPerks.Server.deps.json', 'data', 'icons'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot ('Server\bin\Release\net10.0\' + $name)) -Destination $server -Recurse
}
foreach ($name in @('SeasonalPerks.Client.dll', 'SeasonalPerks.UI.dll', 'SeasonalPerks.Shared.dll', 'seasonalperks_ui.bundle'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot ('Client\bin\Release\netstandard2.1\' + $name)) -Destination $client
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\ui.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\creation-reference.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\skills-reference.md') -Destination $output
foreach ($name in @('LICENSE', 'THIRD_PARTY_NOTICES.md'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output
}
$manifest = Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($output, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
Write-Output "Staged UI update: $output"
