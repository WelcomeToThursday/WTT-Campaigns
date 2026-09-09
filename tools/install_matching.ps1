param([Parameter(Mandatory)][string]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sptRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..\..'))
$packageRoot = (Resolve-Path -LiteralPath $Package).Path.TrimEnd('\')
$serverExe = Join-Path $sptRoot 'SPT_Runtime\SPT.Server.exe'
if (Get-Process EscapeFromTarkov -ErrorAction SilentlyContinue) { throw "Close the game before installing. The complete package remains at $packageRoot." }
$installed = @(Get-CimInstance Win32_Process -Filter "Name='SPT.Server.exe'" | Where-Object { $_.ExecutablePath -eq $serverExe })
if ($installed.Count -ne 1) { throw 'Expected exactly one running server in this installation so its launch settings can be preserved.' }

# Read only the current-directory field from the installed server's process parameters.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SeasonalServerDirectory {
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, int count, out IntPtr read);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr handle, int type, IntPtr data, int size, out int returned);
    static byte[] Read(IntPtr handle, long address, int count) {
        var data = new byte[count];
        if (!ReadProcessMemory(handle, new IntPtr(address), data, count, out var read) || read.ToInt64() != count)
            throw new InvalidOperationException("Cannot capture the installed server working directory.");
        return data;
    }
    public static string Get(int id) {
        if (IntPtr.Size != 8) throw new InvalidOperationException("Use 64-bit PowerShell.");
        var handle = OpenProcess(0x410, false, id);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Cannot read the installed server launch settings.");
        var info = Marshal.AllocHGlobal(48);
        try {
            if (NtQueryInformationProcess(handle, 0, info, 48, out _) != 0) throw new InvalidOperationException("Cannot locate server process parameters.");
            var peb = Marshal.ReadInt64(info, 8);
            var parameters = BitConverter.ToInt64(Read(handle, peb + 0x20, 8), 0);
            var directory = Read(handle, parameters + 0x38, 16);
            var length = BitConverter.ToUInt16(directory, 0);
            if (length == 0 || length > 32766) throw new InvalidOperationException("Invalid server working directory.");
            return Encoding.Unicode.GetString(Read(handle, BitConverter.ToInt64(directory, 8), length));
        } finally { Marshal.FreeHGlobal(info); CloseHandle(handle); }
    }
}
'@
$workingDirectory = [SeasonalServerDirectory]::Get([int]$installed[0].ProcessId)
if (!(Test-Path -LiteralPath $workingDirectory -PathType Container)) { throw 'The original server working directory is unavailable.' }
$arguments = [regex]::Match($installed[0].CommandLine, '^\s*(?:"[^"]+"|\S+)\s*(.*)$', 'Singleline').Groups[1].Value
$required = @(
    'BepInEx\plugins\SeasonalPerks\WTT-Seasonal.Client.dll',
    'BepInEx\plugins\SeasonalPerks\WTT-Seasonal.UI.dll',
    'BepInEx\plugins\SeasonalPerks\WTT-Seasonal.Shared.dll',
    'BepInEx\plugins\SeasonalPerks\seasonalperks_ui.bundle',
    'SPT_Runtime\user\mods\SeasonalPerks\WTT-Seasonal.Server.dll',
    'SPT_Runtime\user\mods\SeasonalPerks\WTT-Seasonal.Shared.dll',
    'SPT_Runtime\user\mods\SeasonalPerks\WTT-Seasonal.Server.deps.json',
    'SPT_Runtime\user\mods\SeasonalPerks\Newtonsoft.Json.dll'
)
$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
$files = foreach ($relative in $required) {
    $entry = @($manifest | Where-Object { $_.path.Replace('/', '\') -eq $relative })
    if ($entry.Count -ne 1) { throw "Missing or duplicated package component: $relative" }
    $source = Join-Path $packageRoot $relative
    $target = Join-Path $sptRoot $relative
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($hash -ne $entry[0].sha256) { throw "Package checksum failed: $relative" }
    $oldHash = if (Test-Path -LiteralPath $target) { (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash } else { '' }
    @{ Relative = $relative; Source = $source; Target = $target; Hash = $hash; OldHash = $oldHash }
}
$changed = @($files | Where-Object { $_.Hash -ne $_.OldHash })
$backup = Join-Path $projectRoot ('Testing\StoryInstallBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
@{ Executable = $serverExe; Arguments = $arguments; WorkingDirectory = $workingDirectory; PreviousProcess = $installed[0].ProcessId; Package = $packageRoot; Files = $files } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $backup 'install.json') -Encoding utf8
$serverProcess = Get-Process -Id $installed[0].ProcessId
if ($serverProcess.Path -ne $serverExe) { throw 'The original installed server changed before shutdown.' }
Stop-Process -Id $serverProcess.Id
Wait-Process -Id $serverProcess.Id -ErrorAction SilentlyContinue
$replaced = @()
try {
    foreach ($file in $changed) {
        $saved = Join-Path $backup $file.Relative
        New-Item -ItemType Directory -Path (Split-Path $saved -Parent) -Force | Out-Null
        if ($file.OldHash) {
            Copy-Item -LiteralPath $file.Target -Destination $saved
            if ((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ne $file.OldHash) { throw 'Backup verification failed.' }
        }
    }
    foreach ($file in $changed) {
        $replaced += $file
        Copy-Item -LiteralPath $file.Source -Destination $file.Target -Force
    }
    foreach ($file in $files) {
        if ((Get-FileHash -LiteralPath $file.Target -Algorithm SHA256).Hash -ne $file.Hash) { throw "Installed checksum mismatch: $($file.Relative)" }
    }
} catch {
    foreach ($file in $replaced) {
        if ($file.OldHash) { Copy-Item -LiteralPath (Join-Path $backup $file.Relative) -Destination $file.Target -Force }
        else { Remove-Item -LiteralPath $file.Target }
    }
    throw
} finally {
    $launch = @{ FilePath = $serverExe; WorkingDirectory = $workingDirectory; WindowStyle = 'Hidden'; PassThru = $true;
        RedirectStandardOutput = (Join-Path $backup 'server.stdout.log'); RedirectStandardError = (Join-Path $backup 'server.stderr.log') }
    if ($arguments.Length) { $launch.ArgumentList = $arguments }
    $restarted = Start-Process @launch
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    while (!(Select-String -LiteralPath $launch.RedirectStandardOutput -Pattern 'Server has started' -Quiet)) {
        if ($restarted.HasExited) { throw "Installed server failed startup. See $backup." }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Installed server startup timed out. See $backup." }
        Start-Sleep -Milliseconds 200
    }
    @{ ProcessId = $restarted.Id; Started = $true; Backup = $backup; Replaced = $changed.Count; Verified = $files.Count } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backup 'result.json') -Encoding utf8
}
Write-Output "Installed $($changed.Count) changed components; verified all $($files.Count). Server restarted and startup verified. Backups: $backup"
