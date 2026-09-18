using System.Xml.Linq;
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
        // Bind the runtime inventory to the actual UXML shipped through the asset
        // builder. A missing/renamed/wrong-type control would otherwise fail on open.
        foreach (var name in EditorLayoutSpec.Sections.Select(n => n.Id))
        {
            using var source =
                typeof(EditorToolkitChecks).Assembly.GetManifestResourceStream("EditorToolkit." + name + ".uxml")
                ?? throw new InvalidOperationException("Missing editor template source: " + name);
            var tree = XDocument.Load(source);
            var elements = tree.Descendants().Where(e => e.Attribute("name") != null).ToDictionary(e => (string)e.Attribute("name")!);
            void Check(EditorLayoutSpec.Node node)
            {
                var tag = node.Kind switch
                {
                    "button" or "choice" => "Button",
                    "input" => "TextField",
                    "text" => "Label",
                    "image" => "Image",
                    "scroll" => "ScrollView",
                    _ => "VisualElement",
                };
                if (!elements.Remove(node.Id, out var element) || element.Name.LocalName != tag)
                    throw new InvalidOperationException("Missing or wrong authored editor control: " + node.Id);
                foreach (var child in node.Children)
                    Check(child);
            }
            Check(EditorLayoutSpec.Sections.Single(n => n.Id == name));
            if (name == "Library")
            {
                foreach (
                    var (id, tag) in new[]
                    {
                        ("BrowserFilters", "VisualElement"),
                        ("BrowserFooter", "VisualElement"),
                        ("ToolActionsScroll", "ScrollView"),
                        ("AiToolsScroll", "ScrollView"),
                        ("BrowserPages", "ScrollView"),
                        ("BrowserTree", "ListView"),
                    }
                )
                    if (!elements.Remove(id, out var slot) || slot.Name.LocalName != tag)
                        throw new InvalidOperationException("Missing or wrong browser slot: " + id);
                var paging = tree.Descendants().Single(e => (string?)e.Attribute("name") == "Paging");
                if (
                    (string?)paging.Parent?.Attribute("name") != "BrowserFooter"
                    || paging.Ancestors().Any(e => e.Name.LocalName == "ScrollView")
                )
                    throw new InvalidOperationException("Browser paging must remain outside the scrolling content.");
            }
            if (name == "CategoryRail" && (!elements.Remove("RailScroll", out var rail) || rail.Name.LocalName != "ScrollView"))
                throw new InvalidOperationException("The tool rail needs its authored scroll container.");
            if (elements.Count != 0)
                throw new InvalidOperationException("Unbound editor controls: " + string.Join(", ", elements.Keys));
        }
        using (var homeSource = typeof(EditorToolkitChecks).Assembly.GetManifestResourceStream("EditorToolkit.Home.uxml")!)
        {
            var home = XDocument
                .Load(homeSource)
                .Descendants()
                .Where(e => e.Attribute("name") != null)
                .ToDictionary(e => (string)e.Attribute("name")!);
            foreach (var item in WTT.Campaigns.UI.Screens.EditorHomeComposition.Elements)
            {
                var tag =
                    item.Kind == "Button" ? "Button"
                    : item.Kind == "Panel" ? "VisualElement"
                    : "Label";
                if (!home.Remove(item.Id, out var element) || element.Name.LocalName != tag)
                    throw new InvalidOperationException("Missing home screen binding: " + item.Id);
            }
            if (home.Count != 0)
                throw new InvalidOperationException("Unbound home screen controls.");
        }
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(game, "EscapeFromTarkov_Data", "Managed"));
        resolver.AddSearchDirectory(Path.GetDirectoryName(clientPath)!);
        using var client = AssemblyDefinition.ReadAssembly(clientPath, new ReaderParameters { AssemblyResolver = resolver });
        foreach (var type in client.MainModule.GetTypes().Where(t => IsAuthoring(t)))
        foreach (var method in type.Methods.Where(m => m.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            if (
                instruction.Operand is GenericInstanceMethod clone
                && clone.DeclaringType.Name == "EditorToolkitDocument"
                && (clone.Name == "Clone" || clone.Name == "CloneTemplate")
                && instruction.Previous?.Operand is string template
            )
            {
                using var source =
                    typeof(EditorToolkitChecks).Assembly.GetManifestResourceStream("EditorToolkit." + template + ".uxml")
                    ?? throw new InvalidOperationException("Missing runtime template: " + template);
                var root = XDocument.Load(source).Root!.Elements().Single(e => e.Name.LocalName != "Style");
                var expected = clone.GenericArguments[0].Name;
                if (expected != "VisualElement" && root.Name.LocalName != expected)
                    throw new InvalidOperationException("Wrong runtime template type: " + template);
            }
            if (
                type.Namespace == "WTT.Campaigns.Client.Authoring.Views"
                && instruction.OpCode.Code == Mono.Cecil.Cil.Code.Newobj
                && instruction.Operand is MethodReference ctor
                && ctor.DeclaringType.Namespace == "UnityEngine.UIElements"
                && new[] { "Button", "Label", "VisualElement", "TextField", "ScrollView", "ListView", "Foldout", "Image" }.Contains(
                    ctor.DeclaringType.Name
                )
            )
                throw new InvalidOperationException("Editor presentation must use authored templates: " + method.FullName);
        }
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
        var lootWindow = EditorLayoutSpec.Sections.Single(n => n.Id == "LootConfiguration");
        if (
            lootWindow.Children.Single().Id != "ContainerScroll"
            || !lootWindow.Children.Single().Children.Any(n => n.Id == "ContainerSettingsGroup")
        )
            throw new InvalidOperationException("Loot configuration must have its own scrollable tool window.");
        var inspector = EditorLayoutSpec.Sections.Single(n => n.Id == "Inspector");
        bool ContainsContainer(EditorLayoutSpec.Node node) => node.Id == "ContainerSettingsGroup" || node.Children.Any(ContainsContainer);
        if (ContainsContainer(inspector))
            throw new InvalidOperationException("Container controls must not crowd the Properties inspector.");
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
        // The typed wrappers also call Get<T>, but their control name is an
        // argument rather than a literal inside Get. Check the container caller
        // against the actual layout, including captions chosen by a branch.
        var editor = client.MainModule.GetType("WTT.Campaigns.Client.Authoring.RaidEditor");
        foreach (var name in new[] { "PresentContainerControls", "BindContainerControls" })
        {
            string? control = null;
            foreach (var instruction in editor.Methods.Single(m => m.Name == name).Body.Instructions)
            {
                if (instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && instruction.Operand is string id && inventory.ContainsKey(id))
                    control = id;
                if (instruction.Operand is not MethodReference call || call.DeclaringType.Name != "RaidEditorView")
                    continue;
                var expected = call.Name switch
                {
                    "Text" => "text",
                    "Caption" or "Button" => "button",
                    "Input" or "Value" => "input",
                    "Dropdown" or "SetDropdown" => "choice",
                    _ => null,
                };
                if (control != null && expected != null && inventory[control] != expected)
                    throw new InvalidOperationException(
                        $"Wrong container control: {control} is {inventory[control]}, but {name} calls {call.Name}."
                    );
                control = null;
            }
        }
        Console.WriteLine(
            $"Editor Toolkit: {references.Length} installed runtime APIs resolve; {inspected} Editor references have no uGUI controls; {nodes.Length} unique controls checked offline."
        );
    }
}
