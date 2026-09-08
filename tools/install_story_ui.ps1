param([string]$Build = (Join-Path $PSScriptRoot '..\Client\bin\Release\netstandard2.1'))
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$install = [IO.Path]::GetFullPath((Join-Path $project '..\..\BepInEx\plugins\SeasonalPerks'))
$Build = [IO.Path]::GetFullPath($Build)
if (Get-Process EscapeFromTarkov -ErrorAction SilentlyContinue) { throw 'Close the game before installing the trader visit update.' }
# This repair changes client presentation only. Require the existing shared contract
# to match, avoiding deployment of server or unrelated story work.
if ((Get-FileHash -LiteralPath (Join-Path $Build 'WTT-Seasonal.Shared.dll')).Hash -ne
    (Get-FileHash -LiteralPath (Join-Path $install 'WTT-Seasonal.Shared.dll')).Hash) {
    throw 'The installed shared contract differs from this build; prepare a matching component update.'
}
$files = @('WTT-Seasonal.Client.dll', 'WTT-Seasonal.UI.dll')
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath (Join-Path $Build $file))) { throw "Missing validated assembly: $file" }
    $target = Join-Path $install $file
    if (Test-Path -LiteralPath $target) {
        try { $probe = [IO.File]::Open($target, 'Open', 'ReadWrite', 'None'); $probe.Dispose() }
        catch { throw "Close the application locking $target before installation can finish." }
    }
}
$backup = Join-Path $project ('Testing\StoryInstallBackups\visit-fix-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $install $file) -Destination (Join-Path $backup $file) }
$manifest = @()
foreach ($file in $files) {
    $source = Join-Path $Build $file
    $target = Join-Path $install $file
    Copy-Item -LiteralPath $source -Destination $target
    $expected = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw "Installed checksum mismatch: $target. Backup: $backup" }
    $manifest += [pscustomobject]@{ path = $target; sha256 = $expected }
}
[pscustomobject]@{ backup = $backup; installed = $manifest; restart = 'Game only' } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backup 'installation.json')
Write-Output "Installed and verified the client and UI assemblies. Backup: $backup. Start the game to use the update; the server can remain running."
