# Synthetic files only. Never creates or starts an SPT runtime.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $projectRoot ('artifacts/rename-deployment-tests/' + [guid]::NewGuid().ToString('N'))
$game = Join-Path $fixture 'game with spaces'
$backups = Join-Path $fixture 'backups'
$oldClient = Join-Path $game 'BepInEx/plugins/SeasonalPerks'
$oldServer = Join-Path $game 'SPT_Runtime/user/mods/SeasonalPerks'
$newClient = Join-Path $game 'BepInEx/plugins/WTT-Campaigns'
$newServer = Join-Path $game 'SPT_Runtime/user/mods/WTT-Campaigns'
$config = Join-Path $game 'BepInEx/config'
$source = Join-Path $fixture 'source'
New-Item -ItemType Directory -Path $oldClient,$oldServer,$source,$config,(Join-Path $oldServer 'creator'),(Join-Path $game 'SPT_Runtime/user/profiles') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $oldClient 'WTT-Seasonal.Client.dll'), 'old client')
[IO.File]::WriteAllText((Join-Path $oldServer 'WTT-Seasonal.Server.dll'), 'old server')
[IO.File]::WriteAllText((Join-Path $oldServer 'config.json'), 'preserve configuration')
[IO.File]::WriteAllText((Join-Path $oldServer 'creator/draft.json'), 'preserve authored content')
[IO.File]::WriteAllText((Join-Path $config 'com.cj.seasonalperks.cfg'), 'preserve client settings')
[IO.File]::WriteAllText((Join-Path $game 'SPT_Runtime/user/profiles/example.json'), 'preserve profiles')
$paths = @('client/WTT-Campaigns.Client.dll','client/WTT-Campaigns.UI.dll','client/WTT-Campaigns.Shared.dll','server/WTT-Campaigns.Server.dll','server/WTT-Campaigns.Shared.dll','server/WTT-Campaigns.Server.deps.json','client/wtt_campaigns_ui.bundle','client/wtt_campaigns_raid_editor.bundle','client/wtt_campaigns_story_notifications.bundle')
$manifest = Join-Path $fixture 'files.props'
$document = [xml]'<Project><ItemGroup /></Project>'
foreach ($path in $paths) {
    $file = Join-Path $source $path
    New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($file, 'validated ' + (Split-Path $file -Leaf))
    $item = $document.CreateElement('CampaignsDeployFile')
    $item.SetAttribute('Include', $file)
    $item.SetAttribute('InstallPath', $path)
    $document.SelectSingleNode('/Project/ItemGroup').AppendChild($item) | Out-Null
}
$document.Save($manifest)
function Check([bool]$Value, [string]$Label) {
    if (!$Value) { throw $Label }
    Write-Host "PASS $Label"
}
function Deploy([string]$Name, [bool]$Pass = $true, [string]$Scope = 'All', [string]$Project = (Join-Path $PSScriptRoot 'install.proj')) {
    $output = & dotnet msbuild $Project -nologo -v:minimal "-p:DeploymentManifest=$manifest" "-p:TarkovDir=$game/" "-p:CampaignsBackupDir=$backups/" "-p:DeploymentScope=$Scope" 2>&1
    $code = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $fixture ($Name + '.log'))
    if (($code -eq 0) -ne $Pass) { throw "$Name returned $code. $($output -join "`n")" }
}
Deploy partial $false UI
Check (!(Test-Path -LiteralPath $newClient)) 'Partial rename is rejected before creating an installation'
$locked = [IO.File]::Open((Join-Path $oldServer 'WTT-Seasonal.Server.dll'), 'Open', 'Read', 'Read')
try {
    Deploy locked $false
    Check ((Test-Path -LiteralPath $oldClient) -and !(Test-Path -LiteralPath $newClient)) 'Locked old server rejects both components before replacement'
} finally { $locked.Dispose() }

# Exercise rollback after the old folders have been archived and the new files copied.
$rollbackProject = Join-Path $fixture 'rollback.proj'
$rollback = [xml]'<Project DefaultTargets="DeployCampaignsFiles" />'
foreach ($import in @((Join-Path $projectRoot 'Directory.Build.props'),$manifest,(Join-Path $PSScriptRoot 'Deployment.targets'))) {
    $element = $rollback.CreateElement('Import'); $element.SetAttribute('Project',$import); $rollback.DocumentElement.AppendChild($element) | Out-Null
}
$target = $rollback.CreateElement('Target'); $target.SetAttribute('Name','ChangeFixtureSource'); $target.SetAttribute('AfterTargets','BackupCampaignsFiles')
$write = $rollback.CreateElement('WriteLinesToFile'); $write.SetAttribute('File',(Join-Path $source $paths[0])); $write.SetAttribute('Lines','changed after hashing'); $write.SetAttribute('Overwrite','true')
$target.AppendChild($write) | Out-Null; $rollback.DocumentElement.AppendChild($target) | Out-Null; $rollback.Save($rollbackProject)
Deploy rollback $false All $rollbackProject
Check ((Test-Path -LiteralPath (Join-Path $oldClient 'WTT-Seasonal.Client.dll')) -and (Test-Path -LiteralPath (Join-Path $oldServer 'WTT-Seasonal.Server.dll'))) 'Failed verification restores both legacy installations'
Check (!(Test-Path -LiteralPath (Join-Path $newClient 'WTT-Campaigns.Client.dll'))) 'Rollback removes new runtime assemblies'
Deploy matching
Check (!(Test-Path -LiteralPath $oldClient) -and !(Test-Path -LiteralPath $oldServer)) 'Old plugin directories retired to prevent duplicate loading'
Check ((Get-Content -LiteralPath (Join-Path $newServer 'config.json') -Raw) -eq 'preserve configuration') 'Server configuration preserved'
Check ((Get-Content -LiteralPath (Join-Path $newServer 'creator/draft.json') -Raw) -eq 'preserve authored content') 'Creator drafts preserved'
Check ((Get-Content -LiteralPath (Join-Path $config 'com.wtt.campaigns.cfg') -Raw) -eq 'preserve client settings') 'Client configuration copied for new GUID'
Check ((Get-Content -LiteralPath (Join-Path $game 'SPT_Runtime/user/profiles/example.json') -Raw) -eq 'preserve profiles') 'Profiles untouched'
foreach ($path in $paths) {
    $root = if ($path.StartsWith('client/')) { $newClient } else { $newServer }
    Check ((Get-FileHash -LiteralPath (Join-Path $root ($path.Split('/')[1]))).Hash -eq (Get-FileHash -LiteralPath (Join-Path $source $path)).Hash) "Installed hash matches $path"
}
$oldBackup = Get-ChildItem -LiteralPath $backups -Filter WTT-Seasonal.Server.dll -Recurse -File
Check (@($oldBackup).Count -ge 1 -and @($oldBackup | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -ne 'old server' }).Count -eq 0) 'Legacy server retained in verified backups, including rollback evidence'
Deploy retry
Write-Host "Rename deployment checks passed. Evidence: $fixture"
