using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Client.Story;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private int _presentedIndexCount = -1;
    private string _passiveState = "";

    private void RefreshPassive()
    {
        if (_view?.Valid != true || _session == null)
            return;
        var state =
            $"{_session.ContentVersion}|{_session.Status}|{_notice}|{_mapScene?.Loading}|{_session.Conflict != null}|{_view.Typing}|{_drag != null}|{_placementLifetime != null}|{_view.Windows.HasMenu}";
        if (_presentedIndexCount != _sceneIndex.Count || state != _passiveState)
        {
            _presentedIndexCount = _sceneIndex.Count;
            _passiveState = state;
            Refresh(false);
        }
    }

    private void RefreshWorkspace()
    {
        var view = _view!;
        view.Text("LibraryHeading", "BROWSER / " + (_mode == "Bindings" ? "EVENTS" : _mode.ToUpperInvariant()));
        view.Value("CameraSpeed", CameraSpeed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        view.Get<Button>("CameraSlower").interactable = CameraSpeed > .25f;
        view.Get<Button>("CameraFaster").interactable = CameraSpeed < 96f;
        var maps = MapWorkspace && EditorMode.Ready;
        var point = _mode == "Scene" ? null : Selected;
        var selection =
            _mode == "Bindings" ? Binding?.Id ?? ""
            : _mode == "Scene" ? (_picked ? _picked!.GetInstanceID().ToString() : "")
            : maps ? MapPoint?.Id ?? MapDoor?.Id ?? Layout?.Id ?? ""
            : point?.Id ?? "";
        if (selection.Length == 0 && _picked && (_mode == "Captures" || _mode == "Bindings"))
            selection = "picked:" + _picked!.GetInstanceID();
        var kind =
            point == null && _picked && !maps && (_mode == "Scene" || _mode == "Captures" || Binding == null) ? "Scene"
            : point is SeasonZone zone ? zone.Shape
            : MapPoint is MapObjectEdit obj ? obj.Operation
            : MapPoint is MapVolume ? (Layout!.Checkpoints.AsValueEnumerable().Any(p => p.Id == _selected) ? "Checkpoint" : "Volume")
            : MapDoor != null ? "Door"
            : maps && MapPoint == null ? "Layout"
            : "Point";
        view.Windows.Present(
            _mode,
            kind,
            selection.Length > 0,
            _task != null,
            _picked,
            EditorMode.Ready,
            point is SeasonZone && Binding != null,
            SceneWorkspace
        );
        if (!SceneWorkspace)
            view.Windows.Select(_mode, selection);
        view.Text(
            "LibraryCount",
            _rows.Count == 0 ? "No matching records" : $"{_rows.Count} records · Page {_page + 1} / {(LibraryTotal + 9) / 10}"
        );
        if (_mode == "Scene" && SceneIndexStatus.Length > 0)
            view.Text("LibraryCount", _sceneIndex.Count + " records · " + (!_sceneIndex.Complete ? "Indexing…" : "Limit reached"));
        view.Get<Button>("Previous").interactable = _page > 0;
        view.Get<Button>("Next").interactable = (_page + 1) * 10 < LibraryTotal;
        for (var i = 0; i < 10; i++)
        {
            var index = LibraryOffset + i;
            var selected =
                index < _rows.Count
                && (
                    _mode == "Bindings" ? _rows[index].Id == Binding?.Id
                    : _mode == "Scene" && !SceneWorkspace
                        ? _picked && int.TryParse(_rows[index].Id, out var sceneIndex) && _sceneIndex.Entries[sceneIndex].Target == _picked
                    : SceneWorkspace && _sceneTab == "Catalog" ? _rows[index].Id == _catalogSelection
                    : SceneWorkspace && _picked ? _rows[index].Id == _picked!.GetInstanceID().ToString() || _rows[index].Id == _selected
                    : _rows[index].Id == _selected
                );
            view.Highlight("Row" + i, selected);
            view.Windows.SetTooltip(
                "Row" + i,
                index >= _rows.Count ? ""
                    : _mode == "Scene" && !SceneWorkspace && int.TryParse(_rows[index].Id, out var tipIndex)
                        ? _sceneIndex.Path(_sceneIndex.Entries[tipIndex].Target) ?? _rows[index].Label
                    : _rows[index].Label
            );
        }
        foreach (var tool in new[] { "Move", "Rotate", "Scale" })
        {
            view.Highlight(tool, _tool == tool);
            if (!SceneWorkspace)
                view.Get<Button>(tool).interactable =
                    point != null
                    && (tool != "Scale" || point is SeasonZone || point is MapVolume || point is MapObjectEdit { Operation: "Copy" });
        }
        view.Highlight("Snap", _snap);
        view.Get<Button>("Parent").interactable = _picked && _picked!.parent;
        view.Text("CaptureRequest", _task == null ? "" : _task.Tool + " capture · " + _notice);
        view.Text(
            "Request",
            (_picking ? "Click scenery · Esc cancel" : "RMB fly · Ctrl+F8 close")
                + (EditorMode.Ready ? " · EDITOR / Gameplay disabled" : " · RAID CONTINUES")
        );
        // Keep the compact status readable; hovering reveals the full operation message.
        view.Text("Status", _session!.Status.Replace("\n", " · ") + (_notice.Length > 0 ? " · " + _notice.Replace("\n", " · ") : ""));
        if (_mode == "Scene" && _picked)
            view.Text("Identity", _picked!.name);
        view.Windows.SetTooltip("Connection", view.Get<Text>("Connection").text);
        view.Windows.SetTooltip("Status", view.Get<Text>("Status").text);
        var canEdit = !_session.Retired && _session.Conflict == null && _session.Definition != null;
        foreach (
            var name in new[]
            {
                "AddBox",
                "AddSphere",
                "Capture",
                "Duplicate",
                "Delete",
                "AtFeet",
                "AtAim",
                "UseObject",
                "EventKind",
                "InZone",
                "VisitPlace",
                "LeaveItemAtLocation",
                "MapNew",
                "MapCopy",
                "MapDelete",
                "MapStart",
                "MapCheckpoint",
                "MapExit",
                "MapBarrier",
                "MapShape",
                "MapMoveObject",
                "MapCopyObject",
                "MapHideObject",
                "MapDoor",
                "MapRebind",
                "MapAtPlayer",
                "MapEarlier",
                "MapLater",
                "Undo",
                "Redo",
            }
        )
            if (!SceneWorkspace || name != "MapAtPlayer")
                view.Get<Button>(name).interactable = canEdit;
        foreach (
            var name in new[]
            {
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
            view.Get<Button>(name).interactable = canEdit && Layout != null;
        view.Get<Button>("UseObject").interactable =
            canEdit && (point is SeasonZone && Binding != null || _picked && _sceneIndex.Complete && !_sceneIndex.Limited);
        var routeIndex = Layout?.Checkpoints.FindIndex(p => p.Id == _selected) ?? -1;
        view.Get<Button>("MapEarlier").interactable = canEdit && routeIndex > 0;
        view.Get<Button>("MapLater").interactable = canEdit && routeIndex >= 0 && routeIndex < Layout!.Checkpoints.Count - 1;
        if (_mode == "Routes")
            view.Get<Button>("MapCopy").interactable = canEdit && routeIndex >= 0;
        view.Get<Button>("EditorWalk").interactable =
            canEdit && Layout != null && !_walkRequested && MapLayoutRules.Errors(Layout, true).Count == 0;
    }
}
