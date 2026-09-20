using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    private void Register(string id, EditorControl control)
    {
        control.Element.name = id;
        if (_registerLocal)
            _toolControls[ToolContext].Add(id, control);
        else
            _controls.Add(id, control);
        if (control is EditorInput input)
            _inputs.Add(input);
        control.Element.RegisterCallback<PointerEnterEvent>(_ => Windows?.ShowTooltip(id, control.Element.tooltip, control.Element));
        control.Element.RegisterCallback<PointerLeaveEvent>(_ => Windows?.HideTooltip());
    }

    private static bool IsWindow(string id) =>
        id is "Library" or "Inspector" or "EnvironmentMenu" or "Controls" or "LootConfiguration" or "Console" or "Navigation";

    private VisualElement BindAuthored(EditorLayoutSpec.Node section, VisualElement parent)
    {
        var root = Document.Clone<VisualElement>(section.Id);
        if (IsWindow(section.Id))
        {
            var shell = Document.Clone<VisualElement>("Window");
            foreach (var className in root.GetClasses())
                shell.AddToClassList(className);
            while (root.childCount > 0)
                shell.Add(root[0]);
            root = shell;
        }
        parent.Add(root);
        void Bind(EditorLayoutSpec.Node node)
        {
            var element = node.Id == section.Id ? root : root.Q(node.Id);
            if (element == null)
                throw new InvalidOperationException("Editor template binding is missing: " + node.Id);
            EditorControl control = node.Kind switch
            {
                "button" or "toggle" => new EditorButton(element),
                "choice" => new EditorChoice((Button)element, OpenChoice),
                "text" => new EditorLabel((Label)element),
                "input" => new EditorInput((TextField)element, node.Id is "Search" or "ConsoleSearch" or "ConsoleCommand"),
                "image" => new EditorImage((Image)element),
                _ => new EditorControl(element),
            };
            Register(node.Id, control);
            // Captions/layout come from UXML; behavior and authored numeric values remain runtime-owned.
            if (control is EditorInput input)
            {
                EditorControlLayout.Field((TextField)element, false);
                input.NumericDrag = EditorNumericDrag.Attach((TextField)element, node.Id);
            }
            if (node.Id.EndsWith("Conflict") && element is TextField conflict)
            {
                conflict.multiline = true;
                conflict.isReadOnly = true;
            }
            if (element is ScrollView scroll)
                EditorScrollStyle.Apply(scroll);
            if (element is Button button)
                button.tooltip = button.text;
            foreach (var child in node.Children)
                Bind(child);
        }
        Bind(section);
        if (section.Id == "Library")
            foreach (var id in new[] { "BrowserFilters", "BrowserFooter", "ToolActionsScroll", "AiToolsScroll" })
            {
                var element = root.Q(id) ?? throw new InvalidOperationException("Missing browser template slot: " + id);
                Register(id, new EditorControl(element));
                if (element is ScrollView scroll)
                    EditorScrollStyle.Apply(scroll);
            }
        return root;
    }

    private void BindWindowChrome(VisualElement window, string id, string title, string closeId)
    {
        var bar = window.Q<VisualElement>("TitleBar");
        var heading = bar.Q<Label>("Heading");
        var close = bar.Q<Button>("Close");
        var resize = window.Q<VisualElement>("Resize");
        heading.text = title;
        Register(id + "Heading", new EditorLabel(heading));
        Register(closeId, new EditorButton(close));
        Register(id + "TitleBar", new EditorControl(bar));
        Register(id + "Resize", new EditorControl(resize));
        resize.BringToFront();
    }

    private void Build()
    {
        var workspace = Document.Clone<VisualElement>("Workspace");
        workspace.AddToClassList("editor-scene-workspace");
        Document.Content.Add(workspace);
        GameViewport.scaleMode = ScaleMode.StretchToFill;
        workspace.Add(GameViewport);
        BuildViewportToolbar(workspace);
        Register("Workspace", new EditorControl(workspace));
        _routeOverlay = new RouteOverlay();
        workspace.Add(_routeOverlay);
        foreach (var section in EditorLayoutSpec.Sections)
        {
            var parent = section.Id == "EditorWalkStatus" || section.Id == "ConflictShield" ? Document.Content : workspace;
            var element = BindAuthored(section, parent);
            if (
                section.Id
                is "Library"
                    or "Inspector"
                    or "EnvironmentMenu"
                    or "Controls"
                    or "LootConfiguration"
                    or "Console"
                    or "Navigation"
            )
            {
                BindWindowChrome(
                    element,
                    section.Id,
                    section.Id == "Library" ? "BROWSER"
                        : section.Id == "Inspector" ? "PROPERTIES"
                        : section.Id == "LootConfiguration" ? "LOOT CONFIGURATION"
                        : section.Id == "Navigation" ? "NAVIGATION"
                        : section.Id == "Console" ? "CONSOLE"
                        : section.Id == "Controls" ? "EDITOR CONTROLS"
                        : "ENVIRONMENT",
                    section.Id == "Library" ? "LibraryCollapse"
                        : section.Id == "Inspector" ? "InspectorCollapse"
                        : section.Id == "LootConfiguration" ? "LootClose"
                        : section.Id == "Navigation" ? "NavigationClose"
                        : section.Id == "Console" ? "ConsoleClose"
                        : section.Id == "Controls" ? "HelpClose"
                        : "EnvironmentClose"
                );
            }
        }
        foreach (
            var (id, hint) in new[]
            {
                ("Move", "Move selected object (W)"),
                ("Rotate", "Rotate selected object (E)"),
                ("Scale", "Scale selected object (R)"),
            }
        )
            Element(id).tooltip = hint;
        Visible("AiTools", false);
        Visible("AiToolsScroll", false);
        foreach (var id in new[] { "WindowsMenu", "ContextMenu" })
        {
            Element(id).AddToClassList("editor-menu");
            Visible(id, false);
        }
        foreach (
            var id in new[]
            {
                "ConflictShield",
                "EditorWalkStatus",
                "CaptureTask",
                "SceneTabs",
                "SceneFilters",
                "SceneInspector",
                "MapInspector",
                "SplineSection",
            }
        )
            Visible(id, false);
        foreach (var input in _inputs)
            if (input.Element.name.StartsWith("Ai"))
                Visible(input.Element.name + "Group", false);
        foreach (var id in RaidEditorAiView.CreationControls)
            Visible(id, false);
        foreach (var id in RaidEditorAiView.InspectorGroups)
            Visible(id, false);
        _browsers.Add("Layouts", new());
        BuildBrowser();
        ApplyIcons();
        BuildTransformToolbar();
        BuildIndependentTools();
        ApplyToolWindowPresentation();
    }

    private void BuildTransformToolbar()
    {
        var toolbar = Element("TransformToolbar");
        var preview = Element("EditorMapToolbar");
        // Keep dividers with their controls so map-only groups hide together.
        foreach (var id in new[] { "Move", "EditorWalk", "AiObserve", "EditorReset" })
        {
            var first = Element(id);
            var separator = Document.Clone<VisualElement>("ToolbarSeparator");
            first.parent.Insert(first.parent.IndexOf(first), separator);
        }

        // Preserve the dock boundary and allow access to the whole row on narrow windows.
        var scroll = Document.Clone<ScrollView>("ToolbarScroll");
        while (toolbar.childCount > 0)
            scroll.Add(toolbar[0]);
        toolbar.Add(scroll);
        scroll.RegisterCallback<WheelEvent>(
            evt =>
            {
                var delta = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y) ? evt.delta.x : evt.delta.y;
                scroll.horizontalScroller.value = Mathf.Clamp(
                    scroll.horizontalScroller.value + delta * 30,
                    scroll.horizontalScroller.lowValue,
                    scroll.horizontalScroller.highValue
                );
                evt.StopPropagation();
            },
            TrickleDown.TrickleDown
        );
    }

    private void ApplyIcons()
    {
        foreach (var pair in EditorToolkitIcons.Names)
        {
            if (
                !(_registerLocal ? _toolControls[ToolContext] : _controls).TryGetValue(pair.Key, out var control)
                || control is not EditorButton button
            )
                continue;
            // Window actions need readable captions; the fixed toolbars retain their icons.
            if (EditorToolWindowStyle.Contains(button.Element) && !pair.Key.EndsWith("Collapse", StringComparison.Ordinal))
                continue;
            var image = Document.Clone<Image>("ToolbarIcon");
            image.sprite = WTT.Campaigns.UI.Media.EditorMaterialArtwork.Load(pair.Value);
            button.text = "";
            button.Element.Add(image);
            button.Element.AddToClassList("editor-icon-button");
        }
    }
}
