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

    private VisualElement BuildNode(EditorLayoutSpec.Node node, VisualElement parent)
    {
        VisualElement element;
        EditorControl control;
        switch (node.Kind)
        {
            case "button":
                var button = new Button { text = node.Text, tooltip = node.Text };
                EditorControlLayout.Action(button);
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
                EditorControlLayout.Action(choice);
                choice.style.minWidth = 160;
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
                EditorScrollStyle.Apply((ScrollView)element);
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
        {
            element.AddToClassList("editor-actions");
            EditorControlLayout.Row(element);
        }
        if (node.Kind == "input")
        {
            element.style.flexShrink = 0;
            EditorControlLayout.Field((TextField)element, false);
            ((EditorInput)control).NumericDrag = EditorNumericDrag.Attach((TextField)element, node.Id);
        }
        if (node.Kind == "group")
            element.AddToClassList("editor-group");
        if (node.Id.EndsWith("Axes"))
            element.AddToClassList("editor-axes");
        foreach (var child in node.Children)
            BuildNode(child, element);
        if (node.Id is "PlacementGroup" or "ZoneUsesGroup" or "SceneActionsGroup" or "RecordActionsGroup")
        {
            element.style.borderTopWidth = 1;
            element.style.borderTopColor = (Color)new Color32(75, 78, 71, 255);
            element.style.marginTop = 8;
            element.style.paddingTop = 8;
            element.style.marginBottom = 4;
            if (node.Kind == "row")
                foreach (var child in element.Children())
                {
                    child.style.flexGrow = 1;
                    child.style.flexBasis = 0;
                    child.style.minWidth = 0;
                }
        }
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
            if (section.Id is "Library" or "Inspector" or "EnvironmentMenu" or "Controls" or "LootConfiguration")
            {
                element.AddToClassList("editor-window");
                element.AddToClassList("editor-surface");
                var bar = new VisualElement();
                bar.AddToClassList("editor-window-title");
                var label = new Label(
                    section.Id == "Library" ? "BROWSER"
                    : section.Id == "Inspector" ? "PROPERTIES"
                    : section.Id == "LootConfiguration" ? "LOOT CONFIGURATION"
                    : section.Id == "Controls" ? "EDITOR CONTROLS"
                    : "ENVIRONMENT"
                );
                label.pickingMode = PickingMode.Ignore;
                Register(section.Id + "Heading", new EditorLabel(label));
                bar.Add(label);
                var closeId =
                    section.Id == "Library" ? "LibraryCollapse"
                    : section.Id == "Inspector" ? "InspectorCollapse"
                    : section.Id == "LootConfiguration" ? "LootClose"
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
        foreach (var id in new[] { "ContainerLootSection", "ContainerFixedSection", "ContainerAccessSection" })
        {
            var section = Element(id);
            section.style.marginBottom = 14;
            section.style.paddingBottom = 12;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = new Color(.3f, .3f, .27f);
        }
        foreach (var id in new[] { "ContainerMode", "ContainerPool", "ContainerItem", "ContainerContents", "ContainerKeyItem" })
        {
            Element(id).style.alignSelf = Align.Stretch;
            Element(id).style.marginBottom = 6;
        }
        Element("ContainerSelection").style.fontSize = 18;
        Element("ContainerSelection").style.marginBottom = 12;
        Element("ContainerScroll").style.paddingLeft = Element("ContainerScroll").style.paddingRight = 12;
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
        var speed = (TextField)Element("CameraSpeed");
        speed.labelElement.style.display = DisplayStyle.None;
        foreach (var id in new[] { "CameraSlower", "CameraSpeed", "CameraFaster" })
        {
            var element = Element(id);
            element.style.alignSelf = Align.Center;
            element.style.height = element.style.minHeight = element.style.maxHeight = 30;
            element.style.marginTop = element.style.marginBottom = 0;
            element.style.marginLeft = element.style.marginRight = 2;
            element.style.paddingTop = element.style.paddingBottom = 0;
            if (id != "CameraSpeed")
            {
                element.style.width = element.style.minWidth = 26;
                element.style.paddingLeft = element.style.paddingRight = 0;
                element.style.unityTextAlign = TextAnchor.MiddleCenter;
            }
        }
        var speedInput = speed.Q(className: "unity-base-text-field__input");
        if (speedInput != null)
            speedInput.style.unityTextAlign = TextAnchor.MiddleLeft;
        foreach (
            var (id, hint) in new[]
            {
                ("Move", "Move selected object (W)"),
                ("Rotate", "Rotate selected object (E)"),
                ("Scale", "Scale selected object (R)"),
            }
        )
            Element(id).tooltip = hint;
        Element("CategoryRail").AddToClassList("editor-grid");
        Element("CreationTools").AddToClassList("editor-grid");
        Visible("AiTools", false);
        // Keep the tree usable even on short windows: tools have their own bounded scroll area.
        var aiToolsScroll = new ScrollView();
        Register("AiToolsScroll", new EditorControl(aiToolsScroll));
        EditorScrollStyle.Apply(aiToolsScroll);
        aiToolsScroll.style.maxHeight = 260;
        aiToolsScroll.style.flexShrink = 1;
        var aiTools = Element("AiTools");
        var aiParent = aiTools.parent;
        var aiIndex = aiParent.IndexOf(aiTools);
        aiParent.Insert(aiIndex, aiToolsScroll);
        aiToolsScroll.Add(aiTools);
        Visible("AiToolsScroll", false);
        foreach (
            var id in new[]
            {
                "AiCreateSection",
                "AiNavigationSection",
                "AiPreviewSection",
                "AiTriggerSection",
                "AiWaveSection",
                "AiRosterSection",
                "AiAssignmentSection",
                "AiPatrolSection",
            }
        )
        {
            Element(id).style.marginTop = 8;
            Element(id).style.paddingTop = 6;
            Element(id).style.borderTopWidth = 1;
            Element(id).style.borderTopColor = new Color(.35f, .35f, .32f, .6f);
            Element(id).style.flexShrink = 0;
        }
        Element("LibraryScroll").style.flexGrow = 1;
        Element("LibraryScroll").style.flexShrink = 1;
        Element("LibraryScroll").style.flexBasis = 0;
        Element("LibraryScroll").style.minHeight = 0;
        Element("LibraryScroll").style.overflow = Overflow.Hidden;
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
        _browsers.Add("Layouts", new());
        BuildBrowser();
        ApplyIcons();
        BuildTransformToolbar();
        BuildIndependentTools();
    }

    private void BuildTransformToolbar()
    {
        var toolbar = Element("TransformToolbar");
        var preview = Element("EditorMapToolbar");
        // Generic action rows wrap, but this bar has only 38px of vertical space.
        foreach (var row in new[] { toolbar, preview })
        {
            row.style.flexWrap = Wrap.NoWrap;
            row.style.alignItems = Align.Center;
        }
        preview.style.maxWidth = StyleKeyword.None;
        preview.style.flexShrink = 0;
        preview.style.height = 30;
        foreach (
            var id in new[]
            {
                "Undo",
                "Redo",
                "Move",
                "Rotate",
                "Scale",
                "Snap",
                "EditorWalk",
                "AiObserve",
                "AiPlaytest",
                "AiPlaytestGear",
                "EditorReset",
            }
        )
        {
            var control = Element(id);
            control.style.alignSelf = Align.Center;
            control.style.height = control.style.minHeight = control.style.maxHeight = 30;
            control.style.marginTop = control.style.marginBottom = 0;
            control.style.whiteSpace = WhiteSpace.NoWrap;
            control.style.maxWidth = StyleKeyword.None;
        }

        // Keep dividers with their controls so map-only groups hide together.
        foreach (var id in new[] { "Move", "CameraSpeedLabel", "EditorWalk", "AiObserve", "EditorReset" })
        {
            var first = Element(id);
            var separator = new VisualElement { pickingMode = PickingMode.Ignore };
            separator.AddToClassList("editor-toolbar-separator");
            separator.style.width = separator.style.minWidth = separator.style.maxWidth = 1;
            separator.style.height = 20;
            separator.style.flexShrink = 0;
            separator.style.alignSelf = Align.Center;
            separator.style.marginLeft = separator.style.marginRight = 6;
            separator.style.backgroundColor = (Color)new Color32(75, 78, 71, 255);
            first.parent.Insert(first.parent.IndexOf(first), separator);
        }

        // Preserve the dock boundary and allow access to the whole row on narrow windows.
        var scroll = new ScrollView(ScrollViewMode.Horizontal)
        {
            horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            verticalScrollerVisibility = ScrollerVisibility.Hidden,
            tooltip = "Scroll to reach more toolbar controls",
        };
        scroll.style.flexGrow = scroll.style.flexShrink = 1;
        scroll.style.minWidth = 0;
        scroll.style.height = 38;
        scroll.contentContainer.style.flexDirection = FlexDirection.Row;
        scroll.contentContainer.style.flexWrap = Wrap.NoWrap;
        scroll.contentContainer.style.alignItems = Align.Center;
        scroll.contentContainer.style.height = 38;
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
