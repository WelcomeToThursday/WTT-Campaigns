using Mono.Cecil;
using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Tests;

internal static class EditorToolkitChecks
{
    private static bool IsAuthoring(TypeDefinition type)
    {
        while (type.DeclaringType != null)
            type = type.DeclaringType;
        return type.Namespace == "WTT.Campaigns.Client.Authoring"
            || type.Namespace.StartsWith("WTT.Campaigns.Client.Authoring.", StringComparison.Ordinal);
    }

    internal static IEnumerable<EditorLayoutSpec.Node> Nodes()
    {
        static IEnumerable<EditorLayoutSpec.Node> Walk(EditorLayoutSpec.Node node)
        {
            yield return node;
            foreach (var child in node.Children)
            foreach (var item in Walk(child))
                yield return item;
        }
        return EditorLayoutSpec.Sections.SelectMany(Walk);
    }

    internal static void Run(string game, string clientPath)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(game, "EscapeFromTarkov_Data", "Managed"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(clientPath)!);
        using var client = AssemblyDefinition.ReadAssembly(clientPath, new ReaderParameters { AssemblyResolver = resolver });
        var references = client
            .MainModule.GetMemberReferences()
            .OfType<MethodReference>()
            .Where(method => method.DeclaringType.Namespace == "UnityEngine.UIElements")
            .ToArray();
        if (references.Length < 20)
            throw new InvalidOperationException("Toolkit runtime references are missing.");
        foreach (var reference in references)
            if (reference.Resolve() == null)
                throw new InvalidOperationException("Installed Unity does not support " + reference.FullName);
        if (client.MainModule.AssemblyReferences.Any(reference => reference.Name.StartsWith("UnityEditor")))
            throw new InvalidOperationException("The runtime client must not depend on Unity Editor assemblies.");
        var document = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.Views.EditorToolkitDocument");
        var constructor = document.Methods.Single(m => m.IsConstructor && !m.IsStatic);
        if (!constructor.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "DontDestroyOnLoad"))
            throw new InvalidOperationException("Toolkit hosts must survive native scene transitions until their owner disposes them.");
        if (
            document
                .Methods.Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "AssetBundle" && m.Name.StartsWith("Unload"))
        )
            throw new InvalidOperationException("Screen teardown must not unload shared Toolkit assets during native scene loading.");
        if (constructor.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "AssetBundle"))
            throw new InvalidOperationException("Document construction must reuse the shared asset cache.");
        var legacy = new[]
        {
            "UiElements",
            "RaidEditorLayout",
            "RaidEditorWindows",
            "EditorDropdown",
            "EditorTreeView",
            "EditorTarkovTheme",
            "EditorRowSelection",
        };
        var types = client.MainModule.GetTypes().Where(t => IsAuthoring(t));
        var inspected = 0;
        foreach (var type in types)
        {
            // EditorMode cooperates with EFT's existing HUD canvases; it renders no controls.
            if (type.Name == "EditorMode" || type.DeclaringType?.Name == "EditorMode")
                continue;
            foreach (var method in type.Methods.Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
            {
                var owner = instruction.Operand switch
                {
                    MemberReference member when member is not TypeReference => member.DeclaringType,
                    TypeReference t => t,
                    _ => null,
                };
                if (owner == null)
                    continue;
                if (owner.Namespace == "UnityEngine.UI" || legacy.Contains(owner.Name))
                    throw new InvalidOperationException("Editor still uses uGUI: " + method.FullName + " -> " + owner.FullName);
                inspected++;
            }
        }
        using var ui = AssemblyDefinition.ReadAssembly(Path.Combine(Path.GetDirectoryName(clientPath)!, "WTT-Campaigns.UI.dll"));
        if (ui.MainModule.GetTypes().Any(t => legacy.Contains(t.Name) && t.Name != "UiElements"))
            throw new InvalidOperationException("Retired uGUI Editor implementations remain in the UI assembly.");
        var nodes = Nodes().ToArray();
        if (nodes.Select(n => n.Id).Distinct().Count() != nodes.Length)
            throw new InvalidOperationException("Duplicate Toolkit control ids.");
        var inventory = nodes.ToDictionary(n => n.Id, n => n.Kind);
        foreach (var id in new[] { "Library", "Inspector", "EnvironmentMenu", "Controls" })
            inventory[id + "Heading"] = "text";
        foreach (var id in new[] { "LibraryCollapse", "InspectorCollapse", "HelpClose", "EnvironmentClose" })
            inventory[id] = "button";
        for (var i = 0; i < 60; i++)
        {
            inventory["Row" + i] = "button";
            inventory["SceneIcon" + i] = "image";
            inventory["SceneIconStatus" + i] = "text";
        }
        foreach (var type in client.MainModule.GetTypes())
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
            if (
                instruction.Operand is GenericInstanceMethod call
                && call.DeclaringType.Name == "RaidEditorView"
                && call.Name == "Get"
                && instruction.Previous?.Operand is string id
            )
            {
                var expected = call.GenericArguments[0].Name switch
                {
                    "EditorButton" => "button",
                    "EditorLabel" => "text",
                    "EditorInput" => "input",
                    "EditorImage" => "image",
                    "EditorChoice" => "choice",
                    _ => "",
                };
                if (!inventory.TryGetValue(id, out var actual) || actual != expected)
                    throw new InvalidOperationException("Missing or wrong Toolkit control: " + id + " in " + method.FullName);
            }
        foreach (var id in new[] { "KeepLocal", "KeepRemote", "Cancel", "Complete", "CloseEditor" })
            if (!nodes.Any(n => n.Id == id && n.Kind == "button"))
                throw new InvalidOperationException("Missing Editor action: " + id);
        Console.WriteLine(
            $"Editor Toolkit: {references.Length} installed runtime APIs resolve; {inspected} Editor references have no uGUI controls; {nodes.Length} unique controls checked offline."
        );
    }
}
