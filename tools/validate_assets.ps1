param([string]$ClientAssembly, [string]$ServerAssembly)
$ErrorActionPreference = 'Stop'
# MSBuild's child shell may inherit a different module search path from its caller.
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
$client = $ClientAssembly
$server = $ServerAssembly
if ($server) {
    $output = Split-Path $server -Parent
    . (Join-Path $PSScriptRoot 'build_helpers.ps1')
    Test-CampaignsServerAssets $output
}
if ($client) {
    $notificationRoot = Split-Path $client -Parent
    $toolkitCheck = Get-Content -LiteralPath (Join-Path $notificationRoot 'editor-toolkit-validation.json') -Raw | ConvertFrom-Json
    if ($toolkitCheck.schema -ne 1 -or !$toolkitCheck.validated -or $toolkitCheck.unity -ne '2022.3.43f1' -or (Get-FileHash -LiteralPath (Join-Path $notificationRoot 'wtt_campaigns_editor_toolkit.bundle') -Algorithm SHA256).Hash -ne $toolkitCheck.sha256) {
        throw 'Editor Toolkit bundle must pass matching Unity SDK import and bundle validation before installation.'
    }
    $notificationCheck = Get-Content -LiteralPath (Join-Path $notificationRoot 'story-notification-validation.json') -Raw | ConvertFrom-Json
    if (@($notificationCheck.prefabs).Count -ne 3 -or @($notificationCheck.dependencies) -notcontains 'wtt_campaigns_ui.bundle') {
        throw 'The chapter notification prefabs have not passed bundle dependency and layout validation.'
    }
    if (@($notificationCheck.bundles.file | Sort-Object) -join ',' -ne 'wtt_campaigns_story_notifications.bundle,wtt_campaigns_ui.bundle') {
        throw 'Chapter notification validation must cover the matching main UI and notification bundles.'
    }
    foreach ($bundle in $notificationCheck.bundles) {
        if ((Get-FileHash -LiteralPath (Join-Path $notificationRoot $bundle.file) -Algorithm SHA256).Hash -ne $bundle.sha256) {
            throw "Chapter notification bundle changed after validation: $($bundle.file)"
        }
    }
    $media = Join-Path (Split-Path $client -Parent) 'StoryMedia'
    $rooms = (Get-Content -LiteralPath (Join-Path $media 'traders.json') -Raw | ConvertFrom-Json).rooms
    if (@($rooms).Count -ne 8 -or @($rooms.trader | Sort-Object -Unique).Count -ne 8) { throw 'Story media requires eight unique trader rooms.' }
    foreach ($room in $rooms) {
        $path = [IO.Path]::GetFullPath((Join-Path $media $room.bundle))
        if (!$path.StartsWith([IO.Path]::GetFullPath($media) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid trader bundle path.' }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $room.sha256) { throw "Trader bundle changed after validation: $($room.trader)" }
        if ((Get-Item -LiteralPath $path).Length -ne $room.bytes) { throw "Trader bundle size differs from the validated manifest: $($room.trader)" }
    }
    $example = Get-Content -LiteralPath (Join-Path $media 'examples/story-test.json') -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath (Join-Path $media 'examples/story-test.bundle') -Algorithm SHA256).Hash -ne $example.sha256) { throw 'The synthetic cinematic has not passed finalization.' }
}
