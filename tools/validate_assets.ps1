param([string]$ClientAssembly, [string]$ServerAssembly)
$ErrorActionPreference = 'Stop'
# MSBuild's child shell may inherit a different module search path from its caller.
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
$client = $ClientAssembly
$server = $ServerAssembly
if ($server) {
    $output = Split-Path $server -Parent
    . (Join-Path $PSScriptRoot 'build_helpers.ps1')
    Test-SeasonalServerAssets $output
}
if ($client) {
    $notificationRoot = Split-Path $client -Parent
    $notificationCheck = Get-Content -LiteralPath (Join-Path $notificationRoot 'story-notification-validation.json') -Raw | ConvertFrom-Json
    if (@($notificationCheck.prefabs).Count -ne 3 -or @($notificationCheck.dependencies) -notcontains 'seasonalperks_ui.bundle') {
        throw 'The chapter notification prefabs have not passed bundle dependency and layout validation.'
    }
    if (@($notificationCheck.bundles.file | Sort-Object) -join ',' -ne 'seasonal_story_notifications.bundle,seasonalperks_ui.bundle') {
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
    }
    $example = Get-Content -LiteralPath (Join-Path $media 'examples/story-test.json') -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath (Join-Path $media 'examples/story-test.bundle') -Algorithm SHA256).Hash -ne $example.sha256) { throw 'The synthetic cinematic has not passed finalization.' }
}
