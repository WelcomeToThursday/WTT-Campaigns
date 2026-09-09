param([string]$ClientAssembly, [string]$ServerAssembly)
$ErrorActionPreference = 'Stop'
$client = $ClientAssembly
$server = $ServerAssembly
if ($server) {
    $output = Split-Path $server -Parent
    . (Join-Path $PSScriptRoot 'build_helpers.ps1')
    Test-SeasonalServerAssets $output
}
if ($client) {
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
