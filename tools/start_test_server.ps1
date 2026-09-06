param([int]$Port = 6975, [switch]$RequireWebLogin)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'build_helpers.ps1')
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$source = Join-Path $sptRoot 'SPT_Runtime'
$staging = Join-Path $projectRoot 'Testing\Server'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
if (Get-Process SPT.Server -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $staging 'SPT.Server.exe') }) { throw 'Stop the isolated server before staging a new build.' }
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Research\UI') -Force | Out-Null

# A separate complete runtime and empty user folder prevent tests touching installed profiles.
& robocopy $source $staging /LEV:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'Runtime copy failed' }
if (!(Test-Path -LiteralPath (Join-Path $staging 'SPT_Data\database')))
{
    & robocopy (Join-Path $source 'SPT_Data') (Join-Path $staging 'SPT_Data') /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw 'SPT data copy failed' }
}

$httpPath = Join-Path $staging 'SPT_Data\configs\http.json'
$http = Get-Content -LiteralPath $httpPath -Raw | ConvertFrom-Json
$http.port = $Port
$http.backendPort = $Port
$http.webAuthenticationConfig.enabled = [bool]$RequireWebLogin
$http.webAuthenticationConfig.requireCredentialsOnLocalhost = [bool]$RequireWebLogin
$http | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $httpPath

dotnet build (Join-Path $projectRoot 'Server') -c Debug --nologo -v:q | Out-Host
if ($LASTEXITCODE) { throw 'Build failed' }
$serverOutput = Get-SeasonalBuildOutput $projectRoot 'Server' 'Debug'
$mod = Join-Path $staging 'user\mods\SeasonalPerks'
New-Item -ItemType Directory -Path $mod -Force | Out-Null
$legacyBackup = Join-Path $projectRoot ('Testing\LegacyBuilds\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
Backup-LegacySeasonalAssemblies $staging $legacyBackup @('user\mods\SeasonalPerks')
Copy-SeasonalServerOutput $serverOutput $mod
$process = Start-Process -FilePath (Join-Path $staging 'SPT.Server.exe') -WorkingDirectory $staging -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $projectRoot 'Research\test-server.stdout.log') -RedirectStandardError (Join-Path $projectRoot 'Research\test-server.stderr.log')
$process.Id | Set-Content -LiteralPath (Join-Path $staging 'test-server.pid')
$deadline = [DateTime]::UtcNow.AddSeconds(30)
do {
    if ($process.HasExited) { throw 'Isolated server exited before startup completed. See Research/test-server logs.' }
    if (Select-String -LiteralPath (Join-Path $projectRoot 'Research\test-server.stdout.log') -Pattern 'Server has started' -Quiet) { break }
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Isolated server did not become ready within 30 seconds.' }
    Start-Sleep -Milliseconds 200
} while ($true)
Write-Output "Isolated test server started on port $Port (process $($process.Id))."
