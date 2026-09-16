using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    internal static readonly string[] ToolIds = { "Layouts", "Routes", "Zones", "Hazards", "Bindings", "Captures", "Scene", "AI" };
    private readonly Dictionary<string, Dictionary<string, EditorControl>> _toolControls = new();
    internal string ToolContext = "Layouts";
    internal Action<string>? ToolActivated;
    private bool _registerLocal;

    internal static string ToolTitle(string id) => id == "Bindings" ? "Events" : id;

    private EditorControl Control(string name) =>
        _toolControls.TryGetValue(ToolContext, out var controls) && controls.TryGetValue(name, out var local) ? local : _controls[name];

    private IEnumerable<(string Tool, EditorControl Control)> Matching(string name)
    {
        if (_controls.TryGetValue(name, out var shared))
            yield return ("", shared);
        foreach (var pair in _toolControls)
            if (pair.Value.TryGetValue(name, out var local))
                yield return (pair.Key, local);
    }

    internal VisualElement WindowElement(string id, string suffix = "") =>
        id.StartsWith("Tool:") ? _toolControls[id.Substring(5)]["Library" + suffix].Element : Element(id + suffix);

    internal bool Activate(string tool)
    {
        if (tool.Length == 0)
            return true;
        if (Windows?.Modal == true)
            return false;
        ToolActivated?.Invoke(tool);
        return ToolContext == tool;
    }

    internal void SearchChanged(Action action)
    {
        foreach (var (tool, control) in Matching("Search"))
            ((EditorInput)control).onValueChanged.AddListener(_ =>
            {
                if (Activate(tool))
                    action();
            });
    }

    private void BuildIndependentTools()
    {
        var original = Element("Library");
        var locals = _controls.AsValueEnumerable().Where(p => p.Value.Element == original || original.Contains(p.Value.Element)).ToArray();
        _toolControls.Add("Layouts", locals.AsValueEnumerable().ToDictionary(p => p.Key, p => p.Value));
        foreach (var pair in locals)
            _controls.Remove(pair.Key);
        var spec = EditorLayoutSpec.Sections.AsValueEnumerable().First(n => n.Id == "Library");
        foreach (var tool in ToolIds)
        {
            ToolContext = tool;
            if (tool != "Layouts")
            {
                _toolControls.Add(tool, new());
                _browsers.Add(tool, new());
                _registerLocal = true;
                var window = BuildNode(spec, Element("Workspace"));
                window.AddToClassList("editor-window");
                window.AddToClassList("editor-surface");
                var title = new VisualElement();
                title.AddToClassList("editor-window-title");
                var label = new Label(ToolTitle(tool).ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                Register("LibraryHeading", new EditorLabel(label));
                title.Add(label);
                var close = new Button { text = "×", tooltip = "Hide window" };
                Register("LibraryCollapse", new EditorButton(close));
                title.Add(close);
                Register("LibraryTitleBar", new EditorControl(title));
                window.Insert(0, title);
                var resize = new VisualElement();
                resize.AddToClassList("editor-resize");
                resize.Add(new Label("◢") { pickingMode = PickingMode.Ignore });
                Register("LibraryResize", new EditorControl(resize));
                window.Add(resize);
                var aiScroll = new ScrollView();
                EditorScrollStyle.Apply(aiScroll);
                Register("AiToolsScroll", new EditorControl(aiScroll));
                var ai = Element("AiTools");
                var index = window.IndexOf(ai);
                window.Insert(index, aiScroll);
                aiScroll.Add(ai);
                BuildBrowser();
                ApplyIcons();
                _registerLocal = false;
            }
            ConfigureToolSurface(tool);
        }
        ToolContext = "Layouts";
        var rail = Element("CategoryRail");
        rail.style.position = Position.Absolute;
        rail.style.left = 8;
        rail.style.top = 84;
        rail.style.width = 40;
        rail.style.bottom = 44;
        rail.style.flexDirection = FlexDirection.Column;
        rail.style.flexWrap = Wrap.NoWrap;
        rail.style.backgroundColor = new Color(.075f, .08f, .075f, .96f);
        var railScroll = new ScrollView();
        EditorScrollStyle.Apply(railScroll);
        railScroll.style.flexGrow = 1;
        foreach (var child in rail.Children().AsValueEnumerable().ToArray())
            railScroll.Add(child);
        rail.Add(railScroll);
    }

    private void ConfigureToolSurface(string tool)
    {
        var window = Element("Library");
        var browser = _browsers[tool];
        window.RegisterCallback<PointerDownEvent>(
            evt =>
            {
                if (evt.target is VisualElement target && ElementForTool(tool, "LibraryScroll").Contains(target))
                    browser.PointerHeld = true;
                Activate(tool);
            },
            TrickleDown.TrickleDown
        );
        window.RegisterCallback<PointerUpEvent>(_ => browser.PointerHeld = false, TrickleDown.TrickleDown);
        window.RegisterCallback<PointerCaptureOutEvent>(_ => browser.PointerHeld = false);
        window.RegisterCallback<DetachFromPanelEvent>(_ => browser.PointerHeld = false);
        window.RegisterCallback<FocusInEvent>(_ => Activate(tool));
        Element("LibraryHeading").tooltip = ToolTitle(tool);
        Text("LibraryHeading", ToolTitle(tool).ToUpperInvariant());
        // Project-style filter toolbar and a single compact status/paging footer.
        var filters = new VisualElement();
        EditorControlLayout.Row(filters);
        window.Insert(window.IndexOf(Element("SceneTabs")), filters);
        filters.Add(Element("SceneTabs"));
        filters.Add(Element("SceneFilters"));
        filters.Add(Element("CatalogViews"));
        filters.Add(Element("Search"));
        foreach (var id in new[] { "SceneTabs", "SceneFilters", "CatalogViews" })
        {
            // Intrinsic groups wrap as units. Percentage constraints on nested
            // auto-sized rows made a short filter group wrap inside itself.
            var group = Element(id);
            group.RemoveFromClassList("editor-actions");
            group.style.maxWidth = StyleKeyword.None;
            group.style.flexWrap = Wrap.NoWrap;
            group.style.flexShrink = 0;
            group.style.alignItems = Align.Center;
            foreach (var child in group.Children())
            {
                child.style.maxWidth = StyleKeyword.None;
                child.style.whiteSpace = WhiteSpace.NoWrap;
            }
        }
        Element("SceneFilters").style.marginLeft = 12;
        Element("CatalogViews").style.marginLeft = 12;
        ((EditorButton)_toolControls[tool]["CatalogGrid"]).onClick.AddListener(() => SetCatalogGrid(tool, true));
        ((EditorButton)_toolControls[tool]["CatalogList"]).onClick.AddListener(() => SetCatalogGrid(tool, false));
        Element("CatalogViews").style.display = DisplayStyle.None;
        var footer = new VisualElement();
        EditorControlLayout.Row(footer);
        window.Insert(window.IndexOf(Element("LibraryCount")), footer);
        footer.Add(Element("LibraryCount"));
        footer.Add(Element("Paging"));
        Element("LibraryCount").style.flexGrow = 1;
        Element("Paging").style.flexWrap = Wrap.NoWrap;
        var list = Element("LibraryScroll");
        list.style.flexGrow = list.style.flexShrink = 1;
        list.style.flexBasis = 0;
        list.style.minHeight = 22;
        list.style.overflow = Overflow.Hidden;
        var actions = new ScrollView();
        EditorScrollStyle.Apply(actions);
        RegisterToolControl(tool, "ToolActionsScroll", new EditorControl(actions));
        actions.style.flexShrink = 1;
        actions.style.minHeight = 0;
        actions.style.maxHeight = 210;
        window.Insert(window.IndexOf(Element("CreationTools")), actions);
        actions.Add(Element("CreationTools"));
        actions.Add(Element("AiToolsScroll"));
        Element("AiToolsScroll").style.maxHeight = StyleKeyword.None;
        Element("CreationTools").AddToClassList("editor-grid");
        foreach (var row in window.Query<VisualElement>(className: "editor-actions").ToList())
            row.style.flexWrap = Wrap.Wrap;
        window.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            actions.style.maxHeight = Math.Max(30, evt.newRect.height * .38f);
            foreach (var field in window.Query<TextField>().ToList())
                EditorControlLayout.Field(field, evt.newRect.width < 340);
        });
        ConfigureToolActions(tool, false, false);
    }

    private VisualElement ElementForTool(string tool, string id) => _toolControls[tool][id].Element;

    private void RegisterToolControl(string tool, string id, EditorControl control)
    {
        control.Element.name = id;
        _toolControls[tool].Add(id, control);
    }

    internal void ConfigureToolActions(string tool, bool mapReady, bool sceneWorkspace)
    {
        void Show(string id, bool show) => _toolControls[tool][id].Visible = show;
        Show("SceneTabs", tool == "Scene" && sceneWorkspace);
        Show("SceneFilters", tool == "Scene" && sceneWorkspace);
        Show("AddBox", tool is "Zones" or "Bindings");
        Show("AddSphere", tool is "Zones" or "Bindings");
        foreach (var hazard in new[] { "Minefield", "Claymore", "Sniper", "BarbedWire" })
            Show("Add" + hazard, tool == "Hazards" && mapReady);
        Show("Capture", tool == "Captures");
        Show("Pick", tool is "Scene" or "Captures" or "Bindings");
        foreach (
            var id in new[]
            {
                "MapNew",
                "MapStart",
                "MapCheckpoint",
                "MapExit",
                "MapBarrier",
                "MapMoveObject",
                "MapCopyObject",
                "MapHideObject",
                "MapDoor",
                "ZoneCreateScope",
            }
        )
            Show(
                id,
                id == "MapNew" ? tool == "Layouts" && mapReady
                    : id == "ZoneCreateScope" ? tool is "Zones" or "Hazards"
                    : id is "MapStart" or "MapCheckpoint" or "MapExit" ? tool == "Routes" && mapReady
                    : tool == "Scene" && sceneWorkspace
            );
        Show("AiTools", tool == "AI" && mapReady);
        Show("AiToolsScroll", tool == "AI" && mapReady);
        foreach (var id in RaidEditorAiView.CreationControls)
            Show(id, tool == "AI" && mapReady);
    }

    internal int VisibleRowCount => _rows.AsValueEnumerable().Count(r => r.Visible);

    internal void SetRowThumbnails(bool thumbnails)
    {
        ApplyCatalogPresentation(thumbnails);
    }
}
