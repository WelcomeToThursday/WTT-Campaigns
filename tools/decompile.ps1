param([Parameter(Mandatory)][string]$Type, [string]$Assembly = 'F:\SPT 4.1.x\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll', [string]$Output)
$ErrorActionPreference = 'Stop'
Add-Type -Path 'F:\Apps\IlSpy\ICSharpCode.Decompiler.dll'
$settings = [ICSharpCode.Decompiler.DecompilerSettings]::new()
$settings.ThrowOnAssemblyResolveErrors = $false
$decompiler = [ICSharpCode.Decompiler.CSharp.CSharpDecompiler]::new($Assembly, $settings)
$result = $decompiler.DecompileTypeAsString([ICSharpCode.Decompiler.TypeSystem.FullTypeName]::new($Type))
if ($Output) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output)) | Out-Null
    [IO.File]::WriteAllText($Output, $result)
} else { $result }
