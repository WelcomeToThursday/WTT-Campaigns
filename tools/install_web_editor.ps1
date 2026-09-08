param(
    [Parameter(Mandatory)][string]$Package,
    [string]$SptRoot = 'F:\SPT 4.1.x',
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$packageRoot = [IO.Path]::GetFullPath($Package)
$sptPath = [IO.Path]::GetFullPath($SptRoot)
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
$allowed = @('WTT-Seasonal.Server.dll', 'wwwroot/creator.css')
if (@($manifest.files).Count -ne $allowed.Count -or @($manifest.files.path | Sort-Object -Unique).Count -ne $allowed.Count) { throw 'Invalid web editor package.' }
$mod = Join-Path $sptPath 'SPT_Runtime/user/mods/SeasonalPerks'
$serverPath = Join-Path $sptPath 'SPT_Runtime/SPT.Server.exe'
if (Get-Process SPT.Server -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $serverPath }) {
    throw 'Close the installed SPT.Server before installing the web editor update. No files have been replaced.'
}
if ((Get-FileHash -LiteralPath (Join-Path $mod 'WTT-Seasonal.Shared.dll') -Algorithm SHA256).Hash -ne $manifest.sharedSha256) {
    throw 'The installed shared assembly changed since package validation. Validate this package against that assembly before installing.'
}
$files = @()
$backup = Join-Path $projectRoot ('Testing/CreatorInstallBackups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
try {
    # Hold exclusive handles on every destination before backing up or replacing any file.
    foreach ($entry in $manifest.files) {
        if ($entry.path -notin $allowed) { throw "Unexpected package file: $($entry.path)" }
        $source = Join-Path $packageRoot $entry.path
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Package checksum failed: $($entry.path)" }
        $target = Join-Path $mod $entry.path
        $handle = [IO.File]::Open($target, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $files += @{ path = $entry.path; source = $source; target = $target; stream = $handle; sha256 = $entry.sha256; previousHash = '' }
    }
    if ($CheckOnly) { Write-Output 'Package verified and destination files are available. No files changed.'; return }
    foreach ($file in $files) {
        $destination = Join-Path $backup $file.path
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        $file.previousHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($file.stream))
        $file.stream.Position = 0
        $backupStream = [IO.File]::Create($destination)
        try { $file.stream.CopyTo($backupStream) } finally { $backupStream.Dispose() }
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $file.previousHash) { throw "Backup verification failed: $($file.path)" }
    }
    try {
        foreach ($file in $files) {
            $bytes = [IO.File]::ReadAllBytes($file.source)
            $file.stream.Position = 0
            $file.stream.Write($bytes)
            $file.stream.SetLength($bytes.Length)
            $file.stream.Flush($true)
            $file.stream.Position = 0
            if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($file.stream)) -ne $file.sha256) { throw "Installed checksum mismatch: $($file.path)" }
        }
    } catch {
        $failure = $_
        foreach ($file in $files) {
            $bytes = [IO.File]::ReadAllBytes((Join-Path $backup $file.path))
            $file.stream.Position = 0
            $file.stream.Write($bytes)
            $file.stream.SetLength($bytes.Length)
            $file.stream.Flush($true)
            $file.stream.Position = 0
            if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($file.stream)) -ne $file.previousHash) { throw "Rollback verification failed. Restore $($file.path) from $backup." }
        }
        throw "Installation failed and previous files were restored. $failure"
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'installed-manifest.json')
    Write-Output "Installed and SHA-256 verified the web editor assembly and stylesheet. Backup: $backup. Start SPT.Server and refresh the editor page."
} finally {
    foreach ($file in $files) { $file.stream.Dispose() }
}
