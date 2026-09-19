using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class HazardNativeChecks
{
    internal static void Run(string game, string clientPath)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(game, "EscapeFromTarkov_Data", "Managed"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(clientPath)!);
        resolver.AddSearchDirectory(Path.Combine(game, "BepInEx", "plugins", "spt"));
        using var client = AssemblyDefinition.ReadAssembly(clientPath, new ReaderParameters { AssemblyResolver = resolver });
        var types = client
            .MainModule.GetTypes()
            .Where(t =>
                (t.Namespace == "WTT.Campaigns.Client.Spatial" || t.DeclaringType?.Namespace == "WTT.Campaigns.Client.Spatial")
                && (
                    t.Name
                        is "HazardRuntime"
                            or "CampaignMinefield"
                            or "CampaignSniperZone"
                            or "CampaignBarbedWire"
                            or "CampaignWireOccupancy"
                            or "HazardAssets"
                    || t.DeclaringType?.Name is "HazardRuntime" or "HazardAssets"
                )
            )
            .ToArray();
        if (types.Length < 5)
            throw new InvalidOperationException("Hazard runtime components are missing.");
        foreach (var type in types)
        {
            if (type.BaseType?.Resolve() == null)
                throw new InvalidOperationException("Unresolved hazard base: " + type.FullName);
            foreach (var instruction in type.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions))
            {
                if (
                    instruction.Operand is MethodReference method
                    && method.DeclaringType.Scope.Name == "Assembly-CSharp"
                    && method.Resolve() == null
                )
                    throw new InvalidOperationException("Missing native hazard method: " + method.FullName);
                if (
                    instruction.Operand is FieldReference field
                    && field.DeclaringType.Scope.Name == "Assembly-CSharp"
                    && field.Resolve() == null
                )
                    throw new InvalidOperationException("Missing native hazard field: " + field.FullName);
            }
        }
        var runtime = types.Single(t => t.Name == "HazardRuntime");
        var clearCalls = runtime
            .Methods.Single(m => m.Name == "Clear")
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>()
            .Select(m => m.Name)
            .ToArray();
        if (!clearCalls.Contains("StopAllCoroutines") || !clearCalls.Contains("ClearOccupants") || !clearCalls.Contains("SetArmed"))
            throw new InvalidOperationException("Hazard cleanup must cancel damage, release occupants and disarm mines.");
        var fixedCalls = runtime
            .Methods.Single(m => m.Name == "FixedUpdate")
            .Body.Instructions.Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        if (
            !fixedCalls.Any(m => m.Name == "Explosion" && m.Parameters.Count == 3)
            || !fixedCalls.Any(m => m.Name == "get_SharedBallisticsCalculator")
        )
            throw new InvalidOperationException("Directional mines must use the current raid's ballistics calculator.");
        var nativeCalls = types
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .ToArray();
        if (!nativeCalls.Any(m => m.Name == "PlayAtPointDistant") || !nativeCalls.Any(m => m.Name == "Retain"))
            throw new InvalidOperationException("Sniper reports require the native positional audio path and a retained sound bundle.");
        if (!clearCalls.Contains("Cancel"))
            throw new InvalidOperationException("Reset must cancel pending hazard resource loads.");
        Console.WriteLine("Hazard native contracts: installed EFT methods, fields, cleanup and raid-local ballistics verified.");
    }
}
