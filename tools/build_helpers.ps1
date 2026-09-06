# Keep build diagnostics off the success stream so package scripts return only their output path.
function Get-SeasonalBuildOutput {
    param([string]$ProjectRoot, [string]$Project, [string]$Configuration = 'Release')
    $projectFile = Join-Path $ProjectRoot "$Project\WTT-Seasonal.$Project.csproj"
    $properties = & dotnet msbuild $projectFile "-p:Configuration=$Configuration" -getProperty:TargetPath,TargetDir
    if ($LASTEXITCODE) { throw "Cannot resolve $Project build output." }
    $properties = ($properties -join "`n" | ConvertFrom-Json).Properties
    if (!(Test-Path -LiteralPath $properties.TargetPath -PathType Leaf)) { throw "Missing build output: $($properties.TargetPath)" }
    return $properties.TargetDir
}

function Copy-SeasonalServerOutput {
    param([string]$Source, [string]$Destination)
    # Never copy the entire bin directory: it can contain DLLs from earlier project names.
    foreach ($name in @('WTT-Seasonal.Server.dll', 'WTT-Seasonal.Shared.dll', 'Newtonsoft.Json.dll', 'WTT-Seasonal.Server.deps.json', 'data', 'icons')) {
        Copy-Item -LiteralPath (Join-Path $Source $name) -Destination $Destination -Recurse -Force
    }
}

function Backup-LegacySeasonalAssemblies {
    param([string]$Root, [string]$BackupRoot, [string[]]$Directories)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $backupPath = [IO.Path]::GetFullPath($BackupRoot).TrimEnd('\', '/')
    foreach ($relative in $Directories) {
        foreach ($project in @('Client', 'UI', 'Server', 'Shared')) {
            foreach ($extension in @('dll', 'pdb', 'deps.json')) {
                $file = Join-Path $relative "SeasonalPerks.$project.$extension"
                $source = [IO.Path]::GetFullPath((Join-Path $rootPath $file))
                $target = [IO.Path]::GetFullPath((Join-Path $backupPath $file))
                if (!$source.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase) -or !$target.StartsWith($backupPath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid legacy assembly backup path.' }
                if (Test-Path -LiteralPath $source -PathType Leaf) {
                    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
                    Move-Item -LiteralPath $source -Destination $target
                }
            }
        }
    }
}
