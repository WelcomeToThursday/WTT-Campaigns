param([string]$Configuration = 'Release')
# Historical name for a matching client/server package. For just the UI assembly,
# use: dotnet msbuild build.proj -p:DeploymentScope=UI
& (Join-Path $PSScriptRoot 'package.ps1') -Configuration $Configuration
