using WTT.Campaigns.Shared.Spatial;
using ZLinq;
using Button = WTT.Campaigns.Client.Authoring.Views.EditorButton;
using InputField = WTT.Campaigns.Client.Authoring.Views.EditorInput;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorMapController
{
    internal void MapRows()
    {
        foreach (
            var layout in _context.Session!.Definition!.MapLayouts.AsValueEnumerable().Where(l => l.Location == _context.Session.Location)
        )
        {
            _context.Rows.Add((layout.Id, "LAYOUT · " + layout.Name + (layout.ApplyInNormalRaids ? " · NORMAL RAIDS" : "")));
            if (layout.Id != _context.LayoutId)
                continue;
            if (_context.ToolId != "Routes")
                continue;
            if (_context.MissionContent && layout.Start != null)
                _context.Rows.Add((layout.Start.Id, "START · " + layout.Start.Name));
            for (var i = 0; _context.MissionContent && i < layout.Checkpoints.Count; i++)
                _context.Rows.Add((layout.Checkpoints[i].Id, $"{i + 1}. {layout.Checkpoints[i].Name}"));
            if (layout.Exit != null)
                _context.Rows.Add((layout.Exit.Id, "EXIT · " + layout.Exit.Name));
        }
    }

    internal void RefreshMaps(bool geometry)
    {
        var view = _context.View!;
        var maps = (_context.MapWorkspace || _context.SceneWorkspace) && EditorMode.Ready;
        // Inspector visibility is owned by the workspace presenter.
        view.Get<Button>("Layouts").interactable = EditorMode.Ready;
        view.Get<Button>("Routes").interactable = EditorMode.Ready;

        view.Visible("EditorMapToolbar", EditorMode.Ready);
        if (EditorMode.Ready)
            view.Text("Request", "EDITOR MODE · Gameplay and progression disabled");
        _context.ReconcileMapPreview();
        if (!maps)
            return;
        view.Value("MapName", MapPoint?.Name ?? MapDoor?.Name ?? _context.Layout?.Name ?? "");
        view.Checked("MapNormalRaid", _context.Layout?.ApplyInNormalRaids == true);
        view.Get<Button>("MapNormalRaid").interactable = _context.Layout != null && !_context.Walking && !_context.AiPreviewBusy;
        if (!_context.SceneWorkspace)
        {
            view.Get<InputField>("MapName").interactable = true;
            view.Get<InputField>("MapName").readOnly = false;
            foreach (var group in new[] { "Position", "Rotation", "Size" })
            foreach (var axis in "XYZ")
            {
                view.Get<InputField>("Map" + group + axis).interactable = true;
                view.Get<InputField>("Map" + group + axis).readOnly = false;
            }
        }
        foreach (var group in new[] { "Position", "Rotation", "Size" })
        {
            var point = MapPoint;
            var vector =
                group == "Position" ? point?.Position
                : group == "Rotation" ? point?.Rotation
                : point is MapVolume v ? v.Size
                : (point as MapObjectEdit)?.Scale;
            for (var i = 0; i < 3; i++)
                view.Value(
                    "Map" + group + "XYZ"[i],
                    (
                        vector == null ? 0
                        : i == 0 ? vector.X
                        : i == 1 ? vector.Y
                        : vector.Z
                    ).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                );
        }
        view.Caption("MapDoor", "Door: " + (MapDoor?.State == "Shut" ? "Closed" : MapDoor?.State ?? "capture"));
        view.Caption("MapShape", "Shape: " + (MapPoint as MapVolume)?.Shape);
        view.Caption("MapAtPlayer", "Under camera");
        view.Checked("MapWalkStart", _context.WalkFromStart);
        if (_context.ToolId == "Routes")
        {
            var index = _context.Layout?.Checkpoints.FindIndex(p => p.Id == _context.SelectionId) ?? -1;
            view.Text(
                "RouteGuide",
                _context.Layout == null
                    ? "Choose a layout here, or create one in Layouts."
                    : "PLAYER ROUTE · "
                        + _context.Layout.Checkpoints.Count
                        + " checkpoints\n"
                        + (
                            index >= 0
                                ? $"Checkpoint {index + 1} / {_context.Layout.Checkpoints.Count} · inserts next"
                                : "Set start → add checkpoints → set exit."
                        )
                        + "\nStart green · Checkpoint amber · End red"
                        + "\nPlaced on floor beneath camera."
            );
        }
        var errors =
            _context.Layout == null
                ? new List<string>
                {
                    _context.ToolId == "Routes"
                        ? "Create a layout in Layouts, then select it here."
                        : "Create or select a layout. Use Routes for player waypoints.",
                }
                : MapLayoutRules.Errors(_context.Layout, _context.MissionContent && _context.ToolId == "Routes");
        if (geometry && !_context.Walking)
        {
            var revision = Newtonsoft.Json.JsonConvert.SerializeObject(_context.Layout);
            if (revision != _context.GhostRevision)
            {
                _context.MapScene ??= new();
                _context.MapScene.Ghosts(_context.Layout, _context.MissionContent);
                _context.GhostRevision = revision;
            }
        }
        if (_context.MapScene != null)
            errors.AddRange(_context.MapScene.TargetErrors);
        if (_context.ToolId == "Layouts")
            errors.AddRange(MapLayerRules.Errors(_context.Session!.Definition!.MapLayouts));
        view.Text(
            "MapDetails",
            (_context.Picked ? "Picked: " + _context.Picked!.name + "\n" : "") + errors.AsValueEnumerable().Take(3).JoinToString("\n")
        );
    }
}
