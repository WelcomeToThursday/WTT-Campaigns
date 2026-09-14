using Mono.Cecil;

namespace WTT.Campaigns.Tests;

internal static class EditorArtworkChecks
{
    private static readonly byte[] PngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    internal static void Run(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var theme = assembly.MainModule.GetType("WTT.Campaigns.UI.Controls.EditorTarkovTheme")
            ?? throw new InvalidOperationException("EditorTarkovTheme is missing from the UI assembly.");
        var mappings = ReadIconMappings(theme);
        var literalLoads = ReadLiteralLoads(assembly);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
            names.Add(mapping.Value);
        foreach (var name in literalLoads)
            names.Add(name);

        var resources = assembly.MainModule.Resources
            .OfType<EmbeddedResource>()
            .ToDictionary(resource => resource.Name, StringComparer.Ordinal);
        var missing = new List<string>();
        var invalid = new List<string>();
        foreach (var name in names.OrderBy(value => value, StringComparer.Ordinal))
        {
            var resourceName = "WTT.Campaigns.Editor." + name + ".png";
            if (!resources.TryGetValue(resourceName, out var resource))
            {
                missing.Add(resourceName);
                continue;
            }
            var bytes = resource.GetResourceData();
            if (bytes.Length < PngSignature.Length || !bytes.Take(PngSignature.Length).SequenceEqual(PngSignature))
                invalid.Add(resourceName);
        }

        if (missing.Count != 0 || invalid.Count != 0)
        {
            var details = new List<string>();
            if (missing.Count != 0)
                details.Add("missing: " + string.Join(", ", missing));
            if (invalid.Count != 0)
                details.Add("invalid PNG signature: " + string.Join(", ", invalid));
            throw new InvalidOperationException(
                "Editor artwork contract failed for " + Path.GetFileName(path) + ": " + string.Join("; ", details)
            );
        }

        Console.WriteLine(
            $"Editor artwork: {mappings.Count} icon mappings and {literalLoads.Count} literal Load references resolved to {names.Count} embedded PNGs in {Path.GetFileName(path)}."
        );
    }

    private static List<(string Key, string Value)> ReadIconMappings(TypeDefinition theme)
    {
        var initializer = theme.Methods.SingleOrDefault(method => method.Name == ".cctor")
            ?? throw new InvalidOperationException("EditorTarkovTheme has no static initializer.");
        var instructions = initializer.Body.Instructions;
        var mappings = new List<(string Key, string Value)>();
        for (var index = 2; index < instructions.Count; index++)
        {
            if (instructions[index].Operand is not MethodReference method
                || method.Name != "set_Item"
                || method.DeclaringType.Name != "Dictionary`2"
                || instructions[index - 2].Operand is not string key
                || instructions[index - 1].Operand is not string value)
                continue;
            mappings.Add((key, value));
        }

        if (mappings.Count == 0)
            throw new InvalidOperationException("EditorTarkovTheme icon mappings were not found in its static initializer.");
        return mappings;
    }

    private static List<string> ReadLiteralLoads(AssemblyDefinition assembly)
    {
        var loads = new List<string>();
        foreach (var type in assembly.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;
                var instructions = method.Body.Instructions;
                for (var index = 1; index < instructions.Count; index++)
                {
                    if (instructions[index].Operand is MethodReference called
                        && called.Name == "Load"
                        && called.DeclaringType.FullName == "WTT.Campaigns.UI.Media.EditorMaterialArtwork"
                        && instructions[index - 1].Operand is string name)
                        loads.Add(name);
                }
            }
        }

        return loads;
    }
}
