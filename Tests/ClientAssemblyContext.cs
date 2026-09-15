using System.Reflection;
using System.Runtime.Loader;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

// Isolate installed Unity libraries from the newer server packages used by the offline test executable.
internal sealed class ClientAssemblyContext(string sptRoot, string clientPath, bool rejectOptionalAi = false)
    : AssemblyLoadContext("WTT-Campaigns client checks")
{
    private readonly string[] _folders =
    [
        Path.GetFullPath(Path.Combine(sptRoot, "BepInEx/plugins/UnityToolkit")),
        Path.GetFullPath(Path.Combine(sptRoot, "BepInEx/plugins/WTT-ClientCommonLib")),
        Path.GetDirectoryName(Path.GetFullPath(clientPath))!,
        Path.GetFullPath(Path.Combine(sptRoot, "BepInEx/DumpedAssemblies/EscapeFromTarkov")),
        Path.GetFullPath(Path.Combine(sptRoot, "BepInEx/core")),
        Path.GetFullPath(Path.Combine(sptRoot, "BepInEx/plugins/spt")),
        Path.GetFullPath(Path.Combine(sptRoot, "EscapeFromTarkov_Data/Managed")),
    ];

    protected override Assembly? Load(AssemblyName name)
    {
        if (rejectOptionalAi && name.Name is "SAIN" or "SAIN.Preset.Shared" or "DrakiaXYZ-BigBrain" or "WTT-Campaigns.AI")
            throw new FileNotFoundException("Optional AI dependency deliberately unavailable: " + name.Name);
        // Share contracts with the fixtures so delegates can exercise the actual client implementation.
        if (name.Name == typeof(StoryResponse).Assembly.GetName().Name)
        {
            return typeof(StoryResponse).Assembly;
        }
        if (name.Name is "mscorlib" or "netstandard" || name.Name!.StartsWith("System", StringComparison.Ordinal))
        {
            return null;
        }
        var path = _folders.Select(folder => Path.Combine(folder, name.Name + ".dll")).FirstOrDefault(File.Exists);
        return path == null ? null : LoadFromAssemblyPath(path);
    }
}
