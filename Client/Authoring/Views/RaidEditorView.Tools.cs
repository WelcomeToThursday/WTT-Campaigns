using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    internal static readonly string[] ToolIds = { "Layouts", "Routes", "Zones", "Hazards", "Bindings", "Captures", "Scene", "AI" };
    private readonly Dictionary<string, Dictionary<string, EditorControl>> _toolControls = new();
    internal WTT.Campaigns.Shared.Authoring.EditorContentMode ContentMode;
    internal bool HasStory;
    internal bool AllowsTool(string tool) => WTT.Campaigns.Shared.Authoring.EditorContentRules.ToolAllowed(ContentMode, tool, HasStory);
    internal bool AllowsAction(string action) => WTT.Campaigns.Shared.Authoring.EditorContentRules.ActionAllowed(ContentMode, action);
    internal string ToolContext = "Layouts";
    internal Action<string>? ToolActivated;
    private bool _registerLocal;

    internal void PresentToolTitle(string tool, string title)
    {
        var heading = (EditorLabel)_toolControls[tool]["LibraryHeading"];
        heading.text = title;
        heading.Element.tooltip = title;
    }

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
        if (!AllowsTool(tool)) return false;
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
                var window = BindAuthored(spec, Element("Workspace"));
                BindWindowChrome(window, "Library", ToolTitle(tool).ToUpperInvariant(), "LibraryCollapse");
                BuildBrowser();
                ApplyIcons();
                _registerLocal = false;
            }
            ConfigureToolSurface(tool);
        }
        ToolContext = "Layouts";
        EditorScrollStyle.Apply(Element("CategoryRail").Q<ScrollView>("RailScroll"));
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
        ((EditorButton)_toolControls[tool]["CatalogGrid"]).onClick.AddListener(() => SetCatalogGrid(tool, true));
        ((EditorButton)_toolControls[tool]["CatalogList"]).onClick.AddListener(() => SetCatalogGrid(tool, false));
        Element("CatalogViews").style.display = DisplayStyle.None;
        var actions = (ScrollView)Element("ToolActionsScroll");
        window.RegisterCallback<GeometryChangedEvent>(evt =>
        {
            actions.style.maxHeight =
                tool == "AI" ? Math.Max(30, evt.newRect.height * .38f)
                : tool == "Scene" ? 54
                : tool is "Hazards" or "Zones" ? Math.Clamp(evt.newRect.height * .35f, 60, 140)
                : Math.Clamp(evt.newRect.height * .2f, 30, 100);
            foreach (var field in window.Query<TextField>().ToList())
                EditorControlLayout.Field(field, evt.newRect.width < 340);
        });
        ConfigureToolActions(tool, false, false);
    }

    internal VisualElement ElementForTool(string tool, string id) => _toolControls[tool][id].Element;

    internal void ConfigureToolActions(string tool, bool mapReady, bool sceneWorkspace)
    {
        void Show(string id, bool show) => _toolControls[tool][id].Visible = show && AllowsAction(id) && AllowsTool(tool);
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
        Show("CreationTools", tool != "AI" && (mapReady || tool is "Zones" or "Bindings" or "Captures"));
        foreach (var id in RaidEditorAiView.CreationControls)
            Show(id, tool == "AI" && mapReady);
    }

    internal int VisibleRowCount => _rows.AsValueEnumerable().Count(r => r.Visible);

    internal void SetRowThumbnails(bool thumbnails)
    {
        ApplyCatalogPresentation(thumbnails);
    }
}
