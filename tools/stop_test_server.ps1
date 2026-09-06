$ErrorActionPreference = 'Stop'
$staging = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Testing\Server'))
$pidFile = Join-Path $staging 'test-server.pid'
if (!(Test-Path -LiteralPath $pidFile)) { return }
$testProcessId = [int](Get-Content -LiteralPath $pidFile)
$process = Get-Process -Id $testProcessId -ErrorAction SilentlyContinue
if ($process)
{
    $expected = Join-Path $staging 'SPT.Server.exe'
    if ($process.Path -ne $expected) { throw 'Recorded PID does not belong to the isolated server.' }
    Stop-Process -Id $testProcessId
    $process.WaitForExit()
}
Remove-Item -LiteralPath $pidFile
