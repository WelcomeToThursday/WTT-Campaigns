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

function Test-SeasonalServerAssets {
    param([string]$Source)
    if (@(Get-ChildItem -LiteralPath (Join-Path $Source 'icons') -Filter '*.png' -File).Count -eq 0) { throw 'Missing perk icons.' }
    foreach ($image in (Get-Content -LiteralPath (Join-Path $Source 'data\hub-images.json') -Raw | ConvertFrom-Json)) {
        if ($image.Id -notmatch '^[0-9a-f]{24}$') { throw 'Invalid hub image identifier.' }
        $path = Join-Path $Source ('hub-images\' + $image.Id + '.png')
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $image.Sha256) {
            throw ('Missing or modified hub image: ' + $image.Id)
        }
    }
}
