$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$bundle = Join-Path $projectRoot 'Client\Resources\seasonalperks_ui.bundle'
if (!(Test-Path -LiteralPath $bundle)) { throw 'Build the recovered UI in CJ-SDK first.' }

dotnet build (Join-Path $projectRoot 'SeasonalPerks.sln') -c Release --nologo -v:q
if ($LASTEXITCODE) { throw 'Release build failed.' }
dotnet run --project (Join-Path $projectRoot 'Tests') -c Release --no-build -- (Join-Path $sptRoot 'BepInEx\DumpedAssemblies\EscapeFromTarkov\Assembly-CSharp.dll')
if ($LASTEXITCODE) { throw 'Contract or client compatibility checks failed.' }
dotnet run --project (Join-Path $projectRoot 'Tests') -c Release --no-build -- --resource-hooks $sptRoot (Join-Path $projectRoot 'Client\bin\Release\netstandard2.1\SeasonalPerks.Client.dll')
if ($LASTEXITCODE) { throw 'Item-resource client hook checks failed.' }

# Unique staging folders preserve previous builds and avoid destructive cleanup.
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$output = Join-Path $projectRoot ('release\SeasonalPerks-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$client = Join-Path $output 'BepInEx\plugins\SeasonalPerks'
$server = Join-Path $output 'SPT_Runtime\user\mods\SeasonalPerks'
New-Item -ItemType Directory -Path $client, $server -Force | Out-Null
foreach ($name in @('SeasonalPerks.Client.dll', 'SeasonalPerks.UI.dll', 'SeasonalPerks.Shared.dll', 'seasonalperks_ui.bundle'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot ('Client\bin\Release\netstandard2.1\' + $name)) -Destination $client
}
foreach ($name in @('SeasonalPerks.Server.dll', 'SeasonalPerks.Shared.dll', 'Newtonsoft.Json.dll', 'SeasonalPerks.Server.deps.json'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot ('Server\bin\Release\net10.0\' + $name)) -Destination $server
}
foreach ($name in @('data', 'icons'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot ('Server\bin\Release\net10.0\' + $name)) -Destination $server -Recurse
}
foreach ($name in @('README.md', 'CONTRIBUTING.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md'))
{
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $output -Recurse
$manifest = Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($output, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
Write-Output "Staged build: $output"
