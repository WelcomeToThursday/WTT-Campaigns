using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class TerrainAssemblyChecks
{
    internal static void Run(string game, string clientPath)
    {
        using var resolver = new DefaultAssemblyResolver();
        var managed = Path.Combine(game, "EscapeFromTarkov_Data", "Managed");
        foreach (
            var folder in new[]
            {
                managed,
                Path.GetDirectoryName(clientPath)!,
                Path.Combine(game, "BepInEx", "plugins", "UnityToolkit"),
                Path.Combine(game, "BepInEx", "core"),
            }
        )
            resolver.AddSearchDirectory(folder);
        using var client = AssemblyDefinition.ReadAssembly(clientPath, new ReaderParameters { AssemblyResolver = resolver });
        using var native = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"));
        using var splat = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "JBooth.MicroSplat.Core.dll"));
        var types = client.MainModule.GetTypes().Where(t => t.FullName.Contains("Authoring.TerrainEditing.")).ToArray();
        var calls = types
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        void Require(bool value, string text)
        {
            if (!value)
                throw new InvalidOperationException("Terrain compatibility: " + text);
        }
        foreach (
            var call in calls.Where(c =>
                c.DeclaringType.Namespace.StartsWith("UnityEngine")
                || c.DeclaringType.Namespace.StartsWith("GPUInstancer")
                || c.DeclaringType.Namespace.StartsWith("JBooth")
            )
        )
            Require(call.Resolve() != null, call.FullName);
        TypeDefinition Native(string name) => native.MainModule.GetTypes().Single(t => t.Name == name);
        foreach (
            var (type, field) in new[]
            {
                ("TerrainBallistic", "_mixData"),
                ("GPUInstancerManager", "spData"),
                ("GPUInstancerTerrainManager", "initalizingInstances"),
                ("GPUInstancerTerrainManager", "replacingInstances"),
            }
        )
            Require(Native(type).Fields.Any(f => f.Name == field), "Installed reflection field " + type + "." + field);
        Require(
            calls.Any(c => c.Name == "GetCell") && calls.All(c => c.Name != "GetDetailMapData"),
            "Grass capture uses actual cached cell dimensions"
        );
        var proxy = Native("GPUInstancerTerrainProxy");
        Require(
            proxy.Fields.Any(f => f.Name == "detailManager") && proxy.Fields.Any(f => f.Name == "detailManagerOptic"),
            "Both native camera grass managers exist"
        );
        var manager = Native("GPUInstancerDetailManager");
        Require(
            manager
                .Methods.Single(m => m.Name == "SetDetailMapData")
                .Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "threadDetailMapData"),
            "Native density setter retains the supplied input maps"
        );
        Require(
            manager
                .Methods.Single(m => m.Name == "InitializeSpatialPartitioning")
                .Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "GenerateCellsInstanceDataFromTerrain"),
            "Native spatial refresh regenerates cells from supplied density data"
        );
        Require(
            calls.Any(c => c.Name == "SetDetailMapData") && calls.Any(c => c.Name == "InitializeSpatialPartitioning"),
            "Client supplies maps and rebuilds spatial data"
        );
        var mix = Native("TerrainBallistic").Methods.Single(m => m.Name == "GetMainTexture");
        Require(
            mix.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_mixData"),
            "Native surface lookup uses the owned mix cache"
        );
        var sync = splat.MainModule.GetTypes().Single(t => t.Name == "MicroSplatTerrain").Methods.Single(m => m.Name == "Sync");
        Require(
            sync.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "customControl0")
                && sync.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_alphamapTextures"),
            "MicroSplat supports both custom and Unity control textures"
        );
        Require(
            calls.All(c => c.DeclaringType.FullName != "System.Threading.Tasks.Task" || c.Name is not ("Delay" or "Yield")),
            "Terrain waits stay on Unity's player loop"
        );
        Console.WriteLine(
            "Terrain native compatibility passed: MicroSplat, GPU grass main/optic, rebuild pipeline, surface lookup, and installed Unity APIs."
        );
    }
}
