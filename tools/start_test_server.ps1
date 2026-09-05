param([int]$Port = 6975)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$source = Join-Path $sptRoot 'SPT_Runtime'
$staging = Join-Path $projectRoot 'Testing\Server'
New-Item -ItemType Directory -Path $staging -Force | Out-Null
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
$http.webAuthenticationConfig.enabled = $false
$http | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $httpPath

dotnet build (Join-Path $projectRoot 'SeasonalPerks.sln') --nologo -v:q
if ($LASTEXITCODE) { throw 'Build failed' }
$mod = Join-Path $staging 'user\mods\SeasonalPerks'
New-Item -ItemType Directory -Path $mod -Force | Out-Null
& robocopy (Join-Path $projectRoot 'Server\bin\Debug\net10.0') $mod /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'Mod staging failed' }
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
