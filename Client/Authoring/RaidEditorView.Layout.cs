using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring;

internal sealed partial class RaidEditorView
{
    private void Register(string id, EditorControl control)
    {
        control.Element.name = id;
        _controls.Add(id, control);
        if (control is EditorInput input)
            _inputs.Add(input);
        control.Element.RegisterCallback<PointerEnterEvent>(_ => Windows?.ShowTooltip(id, control.Element.tooltip));
        control.Element.RegisterCallback<PointerLeaveEvent>(_ => Windows?.HideTooltip());
    }

    private VisualElement BuildNode(EditorLayoutSpec.Node node, VisualElement parent)
    {
        VisualElement element;
        EditorControl control;
        switch (node.Kind)
        {
            case "button":
                var button = new Button { text = node.Text, tooltip = node.Text };
                element = button;
                control = new EditorButton(button);
                break;
            case "input":
                var input = new TextField(node.Text);
                element = input;
                control = new EditorInput(input, node.Id == "Search");
                if (node.Id.EndsWith("Conflict"))
                {
                    input.multiline = true;
                    input.isReadOnly = true;
                    input.style.height = 130;
                }
                break;
            case "choice":
                var choice = new Button { text = node.Text };
                element = choice;
                control = new EditorChoice(choice, OpenChoice);
                break;
            case "text":
                var label = new Label(node.Text);
                label.AddToClassList("editor-label");
                label.pickingMode = PickingMode.Ignore;
                element = label;
                control = new EditorLabel(label);
                break;
            case "image":
                var image = new Image();
                image.AddToClassList("editor-preview");
                element = image;
                control = new EditorImage(image);
                break;
            case "scroll":
                element = new ScrollView();
                element.style.flexGrow = 1;
                element.style.minHeight = 0;
                control = new EditorControl(element);
                break;
            default:
                element = new VisualElement();
                control = new EditorControl(element);
                break;
        }
        Register(node.Id, control);
        parent.Add(element);
        if (node.Kind == "row")
            element.AddToClassList("editor-actions");
        if (node.Kind == "group")
            element.AddToClassList("editor-group");
        if (node.Id.EndsWith("Axes"))
            element.AddToClassList("editor-axes");
        foreach (var child in node.Children)
            BuildNode(child, element);
        return element;
    }

    private void Build()
    {
        var workspace = new VisualElement { pickingMode = PickingMode.Ignore };
        workspace.style.position = Position.Absolute;
        workspace.style.left = workspace.style.right = workspace.style.top = workspace.style.bottom = 0;
        Document.Content.Add(workspace);
        Register("Workspace", new EditorControl(workspace));
        _routeOverlay = new RouteOverlay();
        workspace.Add(_routeOverlay);
        foreach (var section in EditorLayoutSpec.Sections)
        {
            var parent = section.Id == "EditorWalkStatus" || section.Id == "ConflictShield" ? Document.Content : workspace;
            var element = BuildNode(section, parent);
            if (section.Id is "Library" or "Inspector" or "EnvironmentMenu" or "Controls")
            {
                element.AddToClassList("editor-window");
                element.AddToClassList("editor-surface");
                var bar = new VisualElement();
                bar.AddToClassList("editor-window-title");
                var label = new Label(
                    section.Id == "Library" ? "BROWSER"
                    : section.Id == "Inspector" ? "PROPERTIES"
                    : section.Id == "Controls" ? "EDITOR CONTROLS"
                    : "ENVIRONMENT"
                );
                label.pickingMode = PickingMode.Ignore;
                Register(section.Id + "Heading", new EditorLabel(label));
                bar.Add(label);
                var closeId =
                    section.Id == "Library" ? "LibraryCollapse"
                    : section.Id == "Inspector" ? "InspectorCollapse"
                    : section.Id == "Controls" ? "HelpClose"
                    : "EnvironmentClose";
                var close = new Button { text = "×", tooltip = "Hide window" };
                Register(closeId, new EditorButton(close));
                bar.Add(close);
                Register(section.Id + "TitleBar", new EditorControl(bar));
                element.Insert(0, bar);
                var handle = new VisualElement();
                handle.AddToClassList("editor-resize");
                handle.Add(new Label("◢") { pickingMode = PickingMode.Ignore });
                Register(section.Id + "Resize", new EditorControl(handle));
                element.Add(handle);
            }
        }
        foreach (var id in new[] { "WorkspaceTitleBar", "TransformToolbar", "StatusBar", "CaptureTask" })
            Element(id).AddToClassList("editor-toolbar");
        Element("WorkspaceTitleBar").style.top = 0;
        Element("TransformToolbar").style.top = 40;
        Element("StatusBar").style.bottom = 0;
        Element("CaptureTask").style.bottom = 38;
        Element("Connection").style.flexGrow = 1;
        Element("Status").style.flexGrow = 1;
        Element("CaptureRequest").style.flexGrow = 1;
        foreach (var id in new[] { "Connection", "Status", "CaptureRequest", "Request" })
        {
            var style = Element(id).style;
            style.flexShrink = 1;
            style.minWidth = 0;
            style.overflow = Overflow.Hidden;
            style.textOverflow = TextOverflow.Ellipsis;
        }
        Element("CameraSpeed").style.width = 66;
        Element("CameraSpeed").style.flexGrow = 0;
        Element("CategoryRail").AddToClassList("editor-grid");
        Element("CreationTools").AddToClassList("editor-grid");
        Element("LibraryScroll").style.flexGrow = 1;
        Element("LibraryScroll").style.minHeight = 70;
        foreach (var id in new[] { "WindowsMenu", "ContextMenu" })
        {
            Element(id).AddToClassList("editor-menu");
            Visible(id, false);
        }
        Element("ConflictShield").AddToClassList("editor-modal-shield");
        Element("Conflict").AddToClassList("editor-modal");
        Element("EditorWalkStatus").style.position = Position.Absolute;
        Element("EditorWalkStatus").style.left = 16;
        Element("EditorWalkStatus").style.top = 12;
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
        BuildBrowser();
        ApplyIcons();
    }

    private void ApplyIcons()
    {
        foreach (var pair in EditorToolkitIcons.Names)
        {
            if (!_controls.TryGetValue(pair.Key, out var control) || control is not EditorButton button)
                continue;
            var image = new Image
            {
                sprite = WTT.Campaigns.UI.Media.EditorMaterialArtwork.Load(pair.Value),
                pickingMode = PickingMode.Ignore,
            };
            image.style.width = image.style.height = 22;
            button.text = "";
            button.Element.Add(image);
            button.Element.AddToClassList("editor-icon-button");
        }
    }
}
