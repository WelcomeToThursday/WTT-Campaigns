# File-only deployment regression tests. No game/runtime binaries or processes are used.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $projectRoot ('artifacts/deployment-tests/' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$game = Join-Path $fixture 'game with spaces'
$server = Join-Path $fixture 'separate server'
$client = Join-Path $game 'BepInEx/plugins/SeasonalPerks'
$mod = Join-Path $server 'user/mods/SeasonalPerks'
$backups = Join-Path $fixture 'backups'
New-Item -ItemType Directory -Path $source,$client,$mod -Force | Out-Null
$sourceUi = Join-Path $source 'WTT-Seasonal.UI.dll'
$sourceServer = Join-Path $source 'WTT-Seasonal.Server.dll'
$sourceNotification = Join-Path $source 'seasonal_story_notifications.bundle'
$targetUi = Join-Path $client 'WTT-Seasonal.UI.dll'
$targetServer = Join-Path $mod 'WTT-Seasonal.Server.dll'
[IO.File]::WriteAllText($sourceUi, 'validated UI fixture')
[IO.File]::WriteAllText($sourceServer, 'validated server fixture')
[IO.File]::WriteAllText($sourceNotification, 'validated notification prefab fixture')
[IO.File]::WriteAllText($targetUi, 'previous UI fixture')
[IO.File]::WriteAllText((Join-Path $mod 'config.json'), 'preserve config')
New-Item -ItemType Directory -Path (Join-Path $mod 'creator'),(Join-Path $server 'user/profiles') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $mod 'creator/legacy.json'), 'preserve creator')
[IO.File]::WriteAllText((Join-Path $server 'user/profiles/fixture.json'), 'preserve profile')
$manifest = Join-Path $fixture 'files.props'
function Write-Manifest([string]$UiPath = 'client/WTT-Seasonal.UI.dll', [string]$ExpectedHash = '', [switch]$Duplicate) {
    $document = [xml]'<Project><ItemGroup /></Project>'
    $items = @(@{ Source = $sourceUi; Path = $UiPath }, @{ Source = $sourceServer; Path = 'server/WTT-Seasonal.Server.dll' }, @{ Source = $sourceNotification; Path = 'client/seasonal_story_notifications.bundle' })
    if ($Duplicate) { $items += $items[0] }
    foreach ($file in $items) {
        $item = $document.CreateElement('SeasonalDeployFile')
        $item.SetAttribute('Include', $file.Source)
        $item.SetAttribute('InstallPath', $file.Path)
        if ($ExpectedHash) { $item.SetAttribute('ExpectedHash', $ExpectedHash) }
        $document.SelectSingleNode('/Project/ItemGroup').AppendChild($item) | Out-Null
    }
    $document.Save($manifest)
}
function Deploy([bool]$ShouldPass = $true, [string]$Name = 'deploy', [string]$Project = (Join-Path $PSScriptRoot 'install.proj')) {
    $output = & dotnet msbuild $Project -nologo -v:minimal "-p:DeploymentManifest=$manifest" "-p:TarkovDir=$game/" "-p:ServerDir=$server/" "-p:SeasonalBackupDir=$backups/" 2>&1
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $fixture ($Name + '.log'))
    if (($code -eq 0) -ne $ShouldPass) { throw "$Name returned $code. $($output -join "`n")" }
}
function Check([bool]$Value, [string]$Label) {
    if (!$Value) { throw $Label }
    Write-Host "PASS $Label"
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
Write-Manifest
$oldHash = Hash $targetUi
Deploy -Name first
Check ((Hash $sourceUi) -eq (Hash $targetUi) -and (Hash $sourceServer) -eq (Hash $targetServer)) 'Install uses game and separate server paths with spaces'
Check ((Hash $sourceNotification) -eq (Hash (Join-Path $client 'seasonal_story_notifications.bundle'))) 'Dedicated chapter prefab bundle deploys with matching components'
$backup = Get-ChildItem -LiteralPath $backups -Filter 'WTT-Seasonal.UI.dll' -Recurse -File | Select-Object -First 1
Check ((Hash $backup.FullName) -eq $oldHash) 'Previous file backed up byte for byte'
Check ((Get-Content -LiteralPath (Join-Path $mod 'config.json') -Raw) -eq 'preserve config') 'Configuration preserved'
Check ((Get-Content -LiteralPath (Join-Path $mod 'creator/legacy.json') -Raw) -eq 'preserve creator') 'Creator content preserved'
Check ((Get-Content -LiteralPath (Join-Path $server 'user/profiles/fixture.json') -Raw) -eq 'preserve profile') 'Profiles preserved'
$locked = [IO.File]::Open($targetUi, 'Open', 'Read', 'Read')
try {
    Deploy -Name unchanged
    Check ((Get-Content -LiteralPath (Join-Path $fixture 'unchanged.log') -Raw) -match 'Installed 0 changed files') 'Identical locked files skipped'
    [IO.File]::WriteAllText($sourceUi, 'changed UI fixture')
    [IO.File]::WriteAllText($sourceServer, 'changed server fixture')
    $before = Hash $targetServer
    Deploy -ShouldPass $false -Name locked
    Check ((Hash $targetServer) -eq $before) 'Locked destination rejects entire update before replacement'
} finally { $locked.Dispose() }
Write-Manifest -ExpectedHash ('0' * 64)
Deploy -ShouldPass $false -Name checksum
Check ((Hash $targetServer) -eq $before) 'Invalid source checksum rejects update'
Write-Manifest -UiPath 'client/../../outside.dll'
Deploy -ShouldPass $false -Name traversal
Check (!(Test-Path -LiteralPath (Join-Path $game 'BepInEx/outside.dll'))) 'Destination traversal rejected'
Write-Manifest -Duplicate
Deploy -ShouldPass $false -Name duplicate
Check ((Hash $targetServer) -eq $before) 'Duplicate destination rejected'
Write-Manifest -UiPath 'server/config.json'
Deploy -ShouldPass $false -Name config
Check ((Get-Content -LiteralPath (Join-Path $mod 'config.json') -Raw) -eq 'preserve config') 'Configuration cannot enter deployment manifest'
Write-Manifest
# Force a source change after backup, exercising the real verification/rollback targets.
$rollbackProject = Join-Path $fixture 'rollback.proj'
$document = [xml]'<Project DefaultTargets="DeploySeasonalFiles" />'
foreach ($import in @((Join-Path $projectRoot 'Directory.Build.props'),$manifest,(Join-Path $PSScriptRoot 'Deployment.targets'))) {
    $element = $document.CreateElement('Import'); $element.SetAttribute('Project',$import); $document.DocumentElement.AppendChild($element) | Out-Null
}
$target = $document.CreateElement('Target'); $target.SetAttribute('Name','ChangeFixtureSource'); $target.SetAttribute('AfterTargets','BackupSeasonalFiles')
$write = $document.CreateElement('WriteLinesToFile'); $write.SetAttribute('File',$sourceUi); $write.SetAttribute('Lines','source changed during copy'); $write.SetAttribute('Overwrite','true')
$target.AppendChild($write) | Out-Null; $document.DocumentElement.AppendChild($target) | Out-Null; $document.Save($rollbackProject)
$beforeUi = Hash $targetUi
Deploy -ShouldPass $false -Name rollback -Project $rollbackProject
Check ((Hash $targetUi) -eq $beforeUi -and (Hash $targetServer) -eq $before) 'Verification failure restores all previous files'
Deploy -Name retry
Check ((Hash $sourceUi) -eq (Hash $targetUi) -and (Hash $sourceServer) -eq (Hash $targetServer)) 'Retry installs and verifies changed files'
Write-Host "File-only deployment checks passed. Evidence: $fixture"
