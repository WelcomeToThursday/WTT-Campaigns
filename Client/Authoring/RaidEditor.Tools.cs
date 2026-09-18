using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private readonly Dictionary<string, EditorToolSession<UnityEngine.Transform>> _toolStates = new();

    private EditorToolSession<UnityEngine.Transform> ToolStateFor(string tool)
    {
        if (!_toolStates.TryGetValue(tool, out var state))
            _toolStates.Add(tool, state = new());
        return state;
    }

    private List<(string Id, string Label)> _rows => ToolStateFor(_mode).Rows;
    private string _libraryKey
    {
        get => ToolStateFor(_mode).LibraryKey;
        set => ToolStateFor(_mode).LibraryKey = value;
    }

    private void ActivateTool(string tool)
    {
        if (_view == null || !RaidEditorView.ToolIds.AsValueEnumerable().Contains(tool) || _session?.Conflict != null)
            return;
        if (_mode == tool)
        {
            _view.ToolContext = tool;
            return;
        }
        CancelPlacement();
        CancelDrag();
        _picking = false;
        _sceneRebindId = "";
        var previous = ToolStateFor(_mode);
        previous.Selection = _selected;
        previous.Page = _page;
        previous.Picked = _picked;
        _mode = tool;
        _view.ToolContext = tool;
        var next = ToolStateFor(tool);
        _selected = next.Selection;
        _page = next.Page;
        _picked = next.Picked;
        if (tool == "Layouts")
            _selected = _layoutId;
        if (tool == "Routes" && _session?.Definition != null)
            _layoutId = EditorLibraryTrees.RouteOwner(_session.Definition.MapLayouts, _session.Location, _selected) ?? _layoutId;
        ValidateToolSelection();
        _view.Windows.ActiveTool = tool;
        Refresh();
    }

    private void ValidateToolSelection()
    {
        if (_session?.Definition == null)
            return;
        if (_mode == "Bindings" && !(_session.Definition.Story?.RaidBindings.AsValueEnumerable().Any(b => b.Id == _bindingTarget) == true))
        {
            _bindingTarget = "";
            _picked = null;
        }
        if (_selected.Length == 0)
            return;
        var valid = _mode switch
        {
            "AI" => AiSelected(out _).Valid,
            "Layouts" => _session.Definition.MapLayouts.AsValueEnumerable().Any(l => l.Id == _selected),
            "Zones" or "Hazards" => (EditorMode.Ready ? FilterZonesForLayout(_layoutId) : _session.Definition.Zones)
                .AsValueEnumerable()
                .Any(z => z.Id == _selected),
            "Captures" => _session.Definition.Captures.AsValueEnumerable().Any(c => c.Id == _selected),
            "Routes" => _session
                .Definition.MapLayouts.AsValueEnumerable()
                .Any(l => l.Id == _selected || l.Checkpoints.AsValueEnumerable().Any(p => p.Id == _selected))
                || MapPoint != null,
            "Bindings" => _session.Definition.Story?.RaidBindings.AsValueEnumerable().Any(b => b.Id == _bindingTarget) == true,
            "Scene" => !EditorMode.Ready || MapPoint != null || MapDoor != null,
            _ => true,
        };
        var state = ToolStateFor(_mode);
        state.Selection = _selected;
        state.Picked = _picked;
        state.Reconcile(_ => valid);
        _selected = state.Selection;
        _picked = state.Picked;
    }

    private bool _presentingOtherTools;

    private void RefreshOtherToolBrowsers()
    {
        var view = _view!;
        var active = _mode;
        var selection = _selected;
        var page = _page;
        var picked = _picked;
        ToolStateFor(active).Selection = selection;
        ToolStateFor(active).Page = page;
        ToolStateFor(active).Picked = picked;
        _presentingOtherTools = true;
        try
        {
            // Project browser data only; do not run inspector or scene editing paths here.
            foreach (var tool in RaidEditorView.ToolIds)
            {
                if (tool == active || !view.Windows.IsOpen("Tool:" + tool))
                    continue;
                _mode = tool;
                view.ToolContext = tool;
                var state = ToolStateFor(tool);
                _selected = state.Selection;
                _page = state.Page;
                _picked = state.Picked;
                ValidateToolSelection();
                RefreshToolBrowser();
                if (SceneWorkspace)
                    PresentSceneThumbnails(false);
                view.Windows.FitContents();
                state.Selection = _selected;
                state.Page = _page;
            }
        }
        finally
        {
            _mode = active;
            _selected = selection;
            _page = page;
            view.ToolContext = active;
            _picked = picked;
            _presentingOtherTools = false;
        }
    }

    private void RefreshToolActions()
    {
        var view = _view!;
        view.ConfigureToolActions(_mode, EditorMode.Ready, SceneWorkspace);
        view.SetRowThumbnails(SceneWorkspace && _sceneTab == "Catalog");
        view.Visible("SceneFilters", SceneWorkspace);
        view.Caption("AddBox", _mode == "Bindings" ? "+ Trigger" : "+ Box");
        view.Caption("AddSphere", _mode == "Bindings" ? "+ Interaction" : "+ Sphere");
        var editable = _session?.Definition != null && !_session.Retired && _session.Conflict == null;
        foreach (
            var name in new[]
            {
                "AddBox",
                "AddSphere",
                "Capture",
                "Pick",
                "MapNew",
                "MapStart",
                "MapCheckpoint",
                "MapExit",
                "MapBarrier",
                "MapMoveObject",
                "MapCopyObject",
                "MapHideObject",
                "MapDoor",
            }
        )
            view.Get<EditorButton>(name).interactable =
                editable
                && (
                    name is "MapStart" or "MapCheckpoint" or "MapExit" or "MapBarrier" or "MapDoor" ? Layout != null
                    : name is "MapMoveObject" or "MapCopyObject" or "MapHideObject" ? Layout != null && _picked
                    : true
                );
        var scopes = new List<EditorChoice.OptionData> { new("New: Shared") };
        if (EditorMode.Ready && Layout != null)
            scopes.Add(new("New: Layout"));
        view.SetDropdown("ZoneCreateScope", scopes, _zoneCreateShared ? 0 : scopes.Count - 1);
        view.Get<EditorChoice>("ZoneCreateScope").interactable = editable && (_mode is "Zones" or "Hazards");
        foreach (var hazard in HazardRules.Kinds)
            view.Get<EditorButton>("Add" + hazard).interactable = editable && EditorMode.Ready && !AiPreviewBusy;
        view.SetDropdown("AiPlaytestGear", new() { new("Placeholder kit"), new("Copy main-profile kit") }, _aiUseProfileKit ? 1 : 0);
        view.Get<EditorChoice>("AiPlaytestGear").interactable = !AiPreviewBusy;
        view.Get<EditorButton>("AiObserve").interactable = editable && EditorMode.Ready && !_walking && !AiPreviewBusy && Layout != null;
        view.Get<EditorButton>("AiPlaytest").interactable = editable && EditorMode.Ready && !_walking && !AiPreviewBusy && Layout != null;
        if (_mode != "AI")
            return;
        var selected = AiSelected(out _);
        var canAuthor = editable && !AiPreviewBusy && !_session!.Previewing && Layout != null;
        foreach (var name in new[] { "AiEncounter", "AiWave", "AiRoster", "AiSpawn", "AiPatrol", "AiWaypoint" })
            view.Get<EditorButton>(name).interactable = canAuthor;
        view.Get<EditorButton>("AiWave").interactable = canAuthor && selected.Encounter != null;
        view.Get<EditorButton>("AiRoster").interactable = canAuthor && selected.Wave != null;
        view.Get<EditorButton>("AiWaypoint").interactable = canAuthor && selected.Route != null;
        view.Get<EditorButton>("AiReset").interactable = AiPreviewBusy;
        view.Get<EditorButton>("AiSimulate").interactable = editable && _aiPreview;
        view.Caption("AiNavigation", "Inspect navigation: " + (_inspectAiNavigation ? "on" : "off"));
    }
}
