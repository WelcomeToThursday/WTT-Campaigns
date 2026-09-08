# Uses only a dedicated disposable server on port 6988. Installed profile data is never copied.
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$source = [IO.Path]::GetFullPath((Join-Path $project '../../SPT_Runtime'))
$staging = Join-Path $project 'Testing/TypedProfilesServer'
$executable = Join-Path $staging 'SPT.Server.exe'
$stdout = Join-Path $project 'Testing/typed-server.stdout.log'
$stderr = Join-Path $project 'Testing/typed-server.stderr.log'
if (Get-Process SPT.Server -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }) { throw 'Stop the dedicated TypedProfilesServer before running these checks.' }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
robocopy $source $staging /LEV:1 /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'Runtime copy failed' }
if (!(Test-Path (Join-Path $staging 'SPT_Data/database'))) {
    robocopy (Join-Path $source 'SPT_Data') (Join-Path $staging 'SPT_Data') /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw 'Database copy failed' }
}
$httpPath = Join-Path $staging 'SPT_Data/configs/http.json'
$http = Get-Content -LiteralPath $httpPath -Raw | ConvertFrom-Json
$http.port = 6988
$http.backendPort = 6988
$http.webAuthenticationConfig.enabled = $false
$http.webAuthenticationConfig.requireCredentialsOnLocalhost = $false
$http | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $httpPath
$mod = Join-Path $staging 'user/mods/SeasonalPerks'
New-Item -ItemType Directory -Path $mod -Force | Out-Null
robocopy (Join-Path $source 'user/mods/SeasonalPerks') $mod /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'Mod copy failed' }
dotnet build (Join-Path $project 'Server') -c Release --nologo -v:q
if ($LASTEXITCODE) { throw 'Build failed' }
. (Join-Path $PSScriptRoot 'build_helpers.ps1')
$build = Get-SeasonalBuildOutput $project Server
foreach ($name in @('WTT-Seasonal.Server.dll','WTT-Seasonal.Shared.dll','WTT-Seasonal.Server.deps.json')) { Copy-Item -LiteralPath (Join-Path $build $name) -Destination $mod -Force }
$script:testProcess = $null
function Start-TestServer([switch]$ExpectConflict) {
    $script:testProcess = Start-Process -FilePath $executable -WorkingDirectory $staging -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $script:testProcess.Id | Set-Content (Join-Path $staging 'typed-server.pid')
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        $log = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
        if ($ExpectConflict -and $log -match 'Conflicting seasonal profile copies require recovery') { return }
        if (!$ExpectConflict -and $log -match 'Server has started') { return }
        if ($script:testProcess.HasExited) { throw 'Test server exited unexpectedly. See Testing/typed-server logs.' }
        Start-Sleep -Milliseconds 200
    }
    throw 'Test server startup did not reach the expected state.'
}
function Stop-TestServer {
    if ($script:testProcess -and !$script:testProcess.HasExited) {
        if ($script:testProcess.Path -ne $executable) { throw 'Wrong test server process' }
        Stop-Process -Id $script:testProcess.Id
        $script:testProcess.WaitForExit()
    }
}
function Check($value, [string]$label) { if (!$value) { throw $label }; Write-Output "PASS $label" }
function Routes([string]$mode) {
    node (Join-Path $PSScriptRoot 'test_profile_storage.cjs') $mode
    if ($LASTEXITCODE) { throw "Profile routes failed: $mode" }
}
Push-Location $project
try {
    Start-TestServer
    Routes create
    Stop-TestServer
    $state = Get-Content Testing/typed-storage-state.json -Raw | ConvertFrom-Json
    if ($state.child -notmatch '^[a-f0-9]{24}$') { throw 'Invalid synthetic character identity' }
    $seasonal = [IO.Path]::GetFullPath((Join-Path $staging ('user/seasonal/profiles/' + $state.child + '.json')))
    $normal = [IO.Path]::GetFullPath((Join-Path $staging ('user/profiles/' + $state.child + '.json')))
    foreach ($path in @($seasonal, $normal)) { if (!$path.StartsWith($staging + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Profile path outside dedicated test server' } }
    $hash = (Get-FileHash -LiteralPath $seasonal).Hash
    Move-Item -LiteralPath $seasonal -Destination $normal
    Start-TestServer
    $backup = Join-Path $staging ('user/seasonal/migration-backups/' + $state.child + '-' + $hash + '.json')
    Check (((Get-FileHash -LiteralPath $seasonal).Hash -eq $hash) -and ((Get-FileHash -LiteralPath $backup).Hash -eq $hash)) 'Migration and backup preserve exact profile bytes'
    Routes resume
    Stop-TestServer
    Copy-Item -LiteralPath $seasonal -Destination $normal
    Start-TestServer
    Check (!(Test-Path -LiteralPath $normal)) 'Identical interrupted migration resumes safely'
    Stop-TestServer
    Start-TestServer
    $latest = Get-ChildItem (Join-Path $staging 'user/profiles/backups') -Directory | Sort-Object Name -Descending | Select-Object -First 1
    Check (Test-Path -LiteralPath (Join-Path $latest.FullName ($state.child + '.json'))) 'Native startup backup includes separately stored character'
    Stop-TestServer
    Copy-Item -LiteralPath $seasonal -Destination $normal
    [IO.File]::AppendAllText($normal, ' ')
    $conflictHash = (Get-FileHash -LiteralPath $normal).Hash
    $destinationHash = (Get-FileHash -LiteralPath $seasonal).Hash
    Start-TestServer -ExpectConflict
    Stop-TestServer
    Check (((Get-FileHash -LiteralPath $normal).Hash -eq $conflictHash) -and ((Get-FileHash -LiteralPath $seasonal).Hash -eq $destinationHash)) 'Conflicting copies fail without overwriting either profile'
    Remove-Item -LiteralPath $normal
    [IO.File]::WriteAllText($seasonal, '{invalid synthetic test profile')
    Start-TestServer
    Check ((Get-Content -LiteralPath $stdout -Raw) -match 'Profile restored from backup') 'Native backup recovery restores the separate profile'
    Check (!(Test-Path -LiteralPath $normal)) 'Backup recovery does not reintroduce launcher profile'
    Routes cleanup
    Write-Output 'PASS profile storage integration, migration, backup and recovery checks'
} finally {
    Stop-TestServer
    Pop-Location
}
