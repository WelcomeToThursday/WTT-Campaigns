# PowerShell entry points only translate package manifests; MSBuild owns deployment.
. (Join-Path $PSScriptRoot 'build_helpers.ps1')

function Get-SeasonalPaths {
    param([string]$TarkovDir)
    $projectRoot = Split-Path $PSScriptRoot -Parent
    $arguments = @('msbuild', (Join-Path $projectRoot 'build.proj'), '-getProperty:TarkovDir,SeasonalClientDir,SeasonalServerDir,Version')
    if ($TarkovDir) { $arguments += "-p:TarkovDir=$([IO.Path]::GetFullPath($TarkovDir).TrimEnd('\', '/'))/" }
    $result = & dotnet @arguments
    if ($LASTEXITCODE) { throw 'Cannot resolve installation paths through MSBuild.' }
    return ($result -join "`n" | ConvertFrom-Json).Properties
}

function Install-SeasonalFiles {
    param([object[]]$Files, [switch]$CheckOnly, [string]$TarkovDir)
    $projectRoot = Split-Path $PSScriptRoot -Parent
    $manifestDir = Join-Path $projectRoot 'artifacts/deployment'
    New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
    $manifestPath = Join-Path $manifestDir ([guid]::NewGuid().ToString('N') + '.props')
    $document = [xml]'<Project><ItemGroup /></Project>'
    foreach ($file in $Files) {
        $item = $document.CreateElement('SeasonalDeployFile')
        $item.SetAttribute('Include', [IO.Path]::GetFullPath($file.Source))
        $item.SetAttribute('InstallPath', $file.InstallPath)
        $item.SetAttribute('ExpectedHash', $file.Hash)
        $document.SelectSingleNode('/Project/ItemGroup').AppendChild($item) | Out-Null
    }
    $document.Save($manifestPath)
    $arguments = @('msbuild', (Join-Path $PSScriptRoot 'install.proj'), '-nologo', '-v:minimal', "-p:DeploymentManifest=$manifestPath")
    if ($CheckOnly) { $arguments += '-t:CheckSeasonalFiles' }
    if ($TarkovDir) { $arguments += "-p:TarkovDir=$([IO.Path]::GetFullPath($TarkovDir).TrimEnd('\', '/'))/" }
    & dotnet @arguments | Out-Host
    if ($LASTEXITCODE) { throw "MSBuild deployment failed. Validated source files and manifest remain available at $manifestPath. No servers or clients were stopped or started." }
}

function Install-SeasonalPackage {
    param([Parameter(Mandatory)][string]$Package)
    $packageRoot = (Resolve-Path -LiteralPath $Package).Path.TrimEnd('\', '/')
    $manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
    $files = foreach ($entry in $manifest) {
        $relative = $entry.path.Replace('\', '/')
        if ($relative.StartsWith('BepInEx/plugins/SeasonalPerks/')) { $installPath = 'client/' + $relative.Substring('BepInEx/plugins/SeasonalPerks/'.Length) }
        elseif ($relative.StartsWith('SPT_Runtime/user/mods/SeasonalPerks/')) { $installPath = 'server/' + $relative.Substring('SPT_Runtime/user/mods/SeasonalPerks/'.Length) }
        else { continue }
        $source = [IO.Path]::GetFullPath((Join-Path $packageRoot $relative))
        if (!$source.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
        if ($entry.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw "Missing SHA-256: $relative" }
        @{ Source = $source; InstallPath = $installPath; Hash = $entry.sha256 }
    }
    foreach ($required in @('client/WTT-Seasonal.Client.dll', 'client/WTT-Seasonal.UI.dll', 'client/WTT-Seasonal.Shared.dll', 'client/seasonalperks_ui.bundle', 'server/WTT-Seasonal.Server.dll', 'server/WTT-Seasonal.Shared.dll', 'server/WTT-Seasonal.Server.deps.json', 'server/Newtonsoft.Json.dll')) {
        if ($required -notin $files.InstallPath) { throw "Missing package component: $required" }
    }
    $shared = @($files | Where-Object { $_.InstallPath.EndsWith('/WTT-Seasonal.Shared.dll') })
    if ($shared.Count -ne 2 -or $shared[0].Hash -ne $shared[1].Hash) { throw 'Client and server shared contracts do not match.' }
    Install-SeasonalFiles -Files $files
}
