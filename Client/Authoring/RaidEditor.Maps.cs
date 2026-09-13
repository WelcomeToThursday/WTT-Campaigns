using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private string _layoutId = "",
        _ghostRevision = "";
    private bool _walking,
        _walkFromStart;
    private int _checkpoint;
    private Vector3? _returnPosition;
    private Vector2 _returnFacing;
    private MapSceneAdapter? _mapScene;
    private MapLayout? _walkLayout;
    private readonly List<GameObject> _walkHidden = new();
    private MapLayout? Layout => _session?.Definition?.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == _layoutId);
    private SpatialCapture? MapPoint =>
        Layout == null ? null : MapLayoutRules.Points(Layout).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
    private MapDoorEdit? MapDoor => Layout?.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _selected);

    private static string MapId() => Guid.NewGuid().ToString("N").Substring(0, 24);

    private void MapEdit(Action<MapLayout> edit)
    {
        if (!EditorMode.Ready || _walking || Layout == null)
            return;
        _session!.Edit(s => edit(s.MapLayouts.AsValueEnumerable().Single(l => l.Id == _layoutId)));
        _ghostRevision = "";
        Refresh();
    }

    private void BindMapControls(RaidEditorView view)
    {
        void Button(string name, Action action) =>
            view.Button(
                name,
                () =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        _notice = e.Message;
                        Plugin.Error(e);
                    }
                }
            );
        Button(
            "MapNew",
            () =>
            {
                if (!EditorMode.Ready || _walking || _session?.Definition == null)
                    return;
                _layoutId = MapId();
                _selected = _layoutId;
                _session.Edit(s => s.MapLayouts.Add(new MapLayout { Id = _layoutId, Location = _session.Location }));
            }
        );
        Button(
            "MapTool",
            () =>
            {
                var tools = new[] { "Move", "Rotate", "Scale" };
                _tool = tools[(Array.IndexOf(tools, _tool) + 1) % tools.Length];
                Refresh();
            }
        );
        Button(
            "MapSnap",
            () =>
            {
                _snap = !_snap;
                Refresh();
            }
        );
        Button("MapCopy", DuplicateMapRecord);
        Button("MapDelete", DeleteMapRecord);
        Button(
            "MapStart",
            () =>
                MapEdit(l =>
                {
                    l.Start = CapturePoint("Player start");
                    _selected = l.Start.Id;
                })
        );
        Button(
            "MapCheckpoint",
            () =>
                MapEdit(l =>
                {
                    var v = CaptureVolume("Checkpoint " + (l.Checkpoints.Count + 1));
                    l.Checkpoints.Add(v);
                    _selected = v.Id;
                })
        );
        Button(
            "MapExit",
            () =>
                MapEdit(l =>
                {
                    l.Exit = CaptureVolume("Exit");
                    _selected = l.Exit.Id;
                })
        );
        Button(
            "MapBarrier",
            () =>
                MapEdit(l =>
                {
                    var v = CaptureVolume("Barrier");
                    v.Position.Y += 1;
                    l.Barriers.Add(v);
                    _selected = v.Id;
                })
        );
        Button(
            "MapShape",
            () =>
                MapEdit(l =>
                {
                    if (MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected) is MapVolume v)
                        v.Shape = v.Shape == "Box" ? "Sphere" : "Box";
                })
        );
        Button("MapMoveObject", () => CaptureMapObject("Move"));
        Button("MapCopyObject", () => CaptureMapObject("Copy"));
        Button("MapHideObject", () => CaptureMapObject("Hide"));
        Button("MapDoor", ChangeMapDoor);
        Button(
            "MapRebind",
            () =>
                MapEdit(l =>
                {
                    if (!_picked)
                        throw new InvalidOperationException("Pick the replacement scenery first.");
                    var edit = l.Objects.AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
                    if (edit != null)
                        edit.Target = MapSceneAdapter.Capture(_picked!);
                    else if (l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _selected) is { } door)
                        door.Target = MapSceneAdapter.Capture(_picked!, true);
                })
        );
        Button(
            "MapAtPlayer",
            () =>
                MapEdit(l =>
                {
                    var point = MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
                    if (point != null)
                    {
                        point.Position = ZoneRuntime.Vector(_player!.Transform.position);
                        point.Rotation = new SpatialVector { X = _player.Rotation.y, Y = _player.Rotation.x };
                    }
                })
        );
        Button("MapEarlier", () => ReorderCheckpoint(-1));
        Button("MapLater", () => ReorderCheckpoint(1));
        Button(
            "MapWalkStart",
            () =>
            {
                _walkFromStart = !_walkFromStart;
                Refresh();
            }
        );
        Button("EditorWalk", BeginWalkthrough);
        Button(
            "EditorReset",
            () =>
            {
                EndWalkthrough();
                Open();
            }
        );
        Button("EditorUnload", () => EditorMode.Instance.UnloadMap());
        view.Input(
            "MapName",
            text =>
                MapEdit(l =>
                {
                    var point = MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
                    var door = l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _selected);
                    if (point != null)
                        point.Name = text.Trim();
                    else if (door != null)
                        door.Name = text.Trim();
                    else
                        l.Name = text.Trim();
                })
        );
        foreach (var group in new[] { "Position", "Rotation", "Size" })
            for (var i = 0; i < 3; i++)
            {
                var field = group;
                var axis = i;
                view.Input(
                    "Map" + field + "XYZ"[axis],
                    value =>
                        Number(
                            value,
                            number =>
                                MapEdit(l =>
                                {
                                    var point = MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
                                    var vector =
                                        field == "Position" ? point?.Position
                                        : field == "Rotation" ? point?.Rotation
                                        : point is MapVolume volume ? volume.Size
                                        : point is MapObjectEdit { Operation: "Copy" } obj ? obj.Scale
                                        : null;
                                    if (vector == null || field == "Size" && number <= 0)
                                        return;
                                    if (axis == 0)
                                        vector.X = number;
                                    else if (axis == 1)
                                        vector.Y = number;
                                    else
                                        vector.Z = number;
                                    if (field == "Size" && point is MapVolume { Shape: "Sphere" } sphere)
                                    {
                                        sphere.Radius = number / 2;
                                        sphere.Size = new()
                                        {
                                            X = number,
                                            Y = number,
                                            Z = number,
                                        };
                                    }
                                })
                        )
                );
            }
    }

    private SpatialCapture CapturePoint(string name) =>
        new()
        {
            Id = MapId(),
            Name = name,
            Location = _session!.Location,
            Scene = PlayerScene(),
            Position = ZoneRuntime.Vector(_player!.Transform.position),
            Rotation = new SpatialVector { X = _player.Rotation.y, Y = _player.Rotation.x },
        };

    private MapVolume CaptureVolume(string name)
    {
        var p = CapturePoint(name);
        return new MapVolume
        {
            Id = p.Id,
            Name = p.Name,
            Location = p.Location,
            Scene = p.Scene,
            Position = p.Position,
            Rotation = p.Rotation,
        };
    }

    private void CaptureMapObject(string operation)
    {
        if (!_picked)
            throw new InvalidOperationException("Use Pick scene object first.");
        var target = MapSceneAdapter.Capture(_picked!);
        MapEdit(l =>
        {
            var edit = new MapObjectEdit
            {
                Id = MapId(),
                Name = _picked!.name,
                Location = l.Location,
                Scene = target.Scene,
                Target = target,
                Operation = operation,
                Position = ZoneRuntime.Vector(_picked.position),
                Rotation = ZoneRuntime.Vector(_picked.eulerAngles),
                Scale = ZoneRuntime.Vector(_picked.lossyScale),
            };
            if (operation != "Copy")
                l.Objects.RemoveAll(o => o.Operation != "Copy" && o.Target.Scene == target.Scene && o.Target.Path == target.Path);
            l.Objects.Add(edit);
            _selected = edit.Id;
        });
    }

    private void ChangeMapDoor()
    {
        MapEdit(l =>
        {
            var door = l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _selected);
            if (door == null)
            {
                if (!_picked)
                    throw new InvalidOperationException("Pick a native door first.");
                var target = MapSceneAdapter.Capture(_picked!, true);
                door = l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Target.Path == target.Path && d.Target.Scene == target.Scene);
                if (door == null)
                {
                    door = new MapDoorEdit
                    {
                        Id = MapId(),
                        Name = _picked!.name,
                        Target = target,
                    };
                    l.Doors.Add(door);
                }
                _selected = door.Id;
            }
            var states = new[] { "Unchanged", "Open", "Shut", "Locked" };
            door.State = states[(Array.IndexOf(states, door.State) + 1) % states.Length];
        });
    }

    private void ReorderCheckpoint(int direction) =>
        MapEdit(l =>
        {
            var index = l.Checkpoints.FindIndex(p => p.Id == _selected);
            var next = index + direction;
            if (index < 0 || next < 0 || next >= l.Checkpoints.Count)
                return;
            var point = l.Checkpoints[index];
            l.Checkpoints.RemoveAt(index);
            l.Checkpoints.Insert(next, point);
        });

    private void DuplicateMapRecord()
    {
        if (!EditorMode.Ready || _walking)
            return;
        if (_selected == _layoutId && Layout != null)
        {
            var copy = RaidEditorSession.Copy(Layout);
            copy.Id = MapId();
            copy.Name += " copy";
            foreach (var p in MapLayoutRules.Points(copy))
                p.Id = MapId();
            foreach (var d in copy.Doors)
                d.Id = MapId();
            _session!.Edit(s => s.MapLayouts.Add(copy));
            _layoutId = _selected = copy.Id;
        }
        else
            MapEdit(l =>
            {
                var point = MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
                if (point is MapObjectEdit edit)
                {
                    var copy = RaidEditorSession.Copy(edit);
                    copy.Id = MapId();
                    copy.Operation = "Copy";
                    l.Objects.Add(copy);
                    _selected = copy.Id;
                }
                else if (point is MapVolume volume)
                {
                    var copy = RaidEditorSession.Copy(volume);
                    copy.Id = MapId();
                    if (l.Barriers.Contains(volume))
                        l.Barriers.Add(copy);
                    else
                        l.Checkpoints.Add(copy);
                    _selected = copy.Id;
                }
            });
    }

    private void DeleteMapRecord()
    {
        if (!EditorMode.Ready || _walking)
            return;
        if (_selected == _layoutId)
        {
            _session?.Edit(s => s.MapLayouts.RemoveAll(l => l.Id == _layoutId));
            _layoutId = _selected = "";
        }
        else
            MapEdit(l =>
            {
                l.Objects.RemoveAll(p => p.Id == _selected);
                l.Doors.RemoveAll(p => p.Id == _selected);
                l.Barriers.RemoveAll(p => p.Id == _selected);
                l.Checkpoints.RemoveAll(p => p.Id == _selected);
                if (l.Start?.Id == _selected)
                    l.Start = null;
                if (l.Exit?.Id == _selected)
                    l.Exit = null;
                _selected = l.Id;
            });
        Refresh();
    }

    private void MapRows()
    {
        foreach (var layout in _session!.Definition!.MapLayouts.AsValueEnumerable().Where(l => l.Location == _session.Location))
        {
            _rows.Add((layout.Id, "LAYOUT · " + layout.Name));
            if (layout.Id != _layoutId)
                continue;
            if (layout.Start != null)
                _rows.Add((layout.Start.Id, "Start · " + layout.Start.Name));
            foreach (var o in layout.Objects)
                _rows.Add((o.Id, o.Operation + " · " + o.Name));
            foreach (var d in layout.Doors)
                _rows.Add((d.Id, "Door · " + d.Name + " · " + d.State));
            foreach (var b in layout.Barriers)
                _rows.Add((b.Id, "Barrier · " + b.Name));
            for (var i = 0; i < layout.Checkpoints.Count; i++)
                _rows.Add((layout.Checkpoints[i].Id, (i + 1) + ". " + layout.Checkpoints[i].Name));
            if (layout.Exit != null)
                _rows.Add((layout.Exit.Id, "Exit · " + layout.Exit.Name));
        }
    }

    private void RefreshMaps(bool geometry)
    {
        var view = _view!;
        var maps = _mode == "Maps" && EditorMode.Ready;
        view.Get<Transform>("MapInspector").gameObject.SetActive(maps);
        view.Get<Button>("Maps").interactable = EditorMode.Ready;
        foreach (var name in new[] { "AddBox", "AddSphere", "Capture" })
            view.Get<Button>(name).interactable = !maps;
        view.Get<Transform>("EditorMapToolbar").gameObject.SetActive(EditorMode.Ready);
        if (EditorMode.Ready)
            view.Text("Request", "EDITOR MODE · Gameplay and progression disabled");
        if (!maps)
            return;
        view.Value("MapName", MapPoint?.Name ?? MapDoor?.Name ?? Layout?.Name ?? "");
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
        view.Caption("MapTool", "Tool: " + _tool);
        view.Caption("MapSnap", "Snap: " + (_snap ? "on" : "off"));
        view.Caption("MapDoor", "Door: " + (MapDoor?.State == "Shut" ? "Closed" : MapDoor?.State ?? "capture"));
        view.Caption("MapShape", "Shape: " + (MapPoint as MapVolume)?.Shape);
        view.Caption("MapWalkStart", "Walk from marker: " + (_walkFromStart ? "on" : "off"));
        var errors =
            Layout == null
                ? new List<string> { "Create or select a layout. Pick scenery to capture props or doors." }
                : MapLayoutRules.Errors(Layout, true);
        if (geometry && !_walking)
        {
            var revision = Newtonsoft.Json.JsonConvert.SerializeObject(Layout);
            if (revision != _ghostRevision)
            {
                _mapScene ??= new();
                _mapScene.Ghosts(Layout);
                _ghostRevision = revision;
            }
        }
        if (_mapScene != null)
            errors.AddRange(_mapScene.TargetErrors);
        view.Text("MapDetails", (_picked ? "Picked: " + _picked!.name + "\n" : "") + errors.AsValueEnumerable().Take(3).JoinToString("\n"));
    }

    private static bool ClearPosition(Vector3 position, EFT.Player player)
    {
        var colliders = Physics.OverlapCapsule(
            position + Vector3.up * .4f,
            position + Vector3.up * 1.45f,
            .3f,
            ~0,
            QueryTriggerInteraction.Ignore
        );
        return !colliders.AsValueEnumerable().Any(c => c && c.GetComponentInParent<EFT.Player>() != player)
            && Physics.Raycast(position + Vector3.up * .15f, Vector3.down, 1.5f, ~0, QueryTriggerInteraction.Ignore);
    }

    private void BeginWalkthrough()
    {
        if (!EditorMode.Ready || _walking || Layout == null || _session?.Conflict != null || _session?.Busy == true)
            return;
        _mapScene?.Dispose();
        _mapScene = new();
        try
        {
            _walkLayout = RaidEditorSession.Copy(Layout);
            _mapScene.Apply(_walkLayout);
            if (_walkFromStart)
            {
                var destination = ZoneRuntime.Vector(_walkLayout.Start!.Position);
                if (!ClearPosition(destination, _player!))
                    throw new InvalidOperationException("The start marker needs clear standing room and a floor.");
                _returnPosition = _player!.Transform.position;
                _returnFacing = _player.Rotation;
                _player.Teleport(destination);
                _player.Rotation = new Vector2(_walkLayout.Start.Rotation.Y, _walkLayout.Start.Rotation.X);
            }
            _walking = true;
            _checkpoint = 0;
            _session!.Hold = true;
            Close();
            _view!.Root.SetActive(true);
            foreach (Transform panel in _view.Root.transform)
                if (panel.gameObject.activeSelf && panel.name != "EditorMapToolbar")
                {
                    _walkHidden.Add(panel.gameObject);
                    panel.gameObject.SetActive(false);
                }
            _view.Get<Transform>("EditorMapToolbar").gameObject.SetActive(true);
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        catch
        {
            EndWalkthrough();
            throw;
        }
    }

    private void UpdateWalkthrough()
    {
        if (!_walking)
            return;
        if (
            !EditorMode.Ready
            || _session?.Grant.Length == 0
            || _session?.Conflict != null
            || !_player
            || Input.GetKeyDown(KeyCode.Escape)
            || _shortcut.Value.IsDown()
        )
        {
            EndWalkthrough();
            return;
        }
        var route = _walkLayout!;
        var target = _checkpoint < route.Checkpoints.Count ? route.Checkpoints[_checkpoint] : route.Exit;
        if (target != null && _checkpoint <= route.Checkpoints.Count && Inside(ToZone(target), _player!.Transform.position))
            _checkpoint++;
        if (_view?.Valid == true)
            _view.Text(
                "EditorWalkStatus",
                (
                    _checkpoint > route.Checkpoints.Count
                        ? "Route complete"
                        : "Checkpoint " + (_checkpoint + 1) + " / " + route.Checkpoints.Count
                ) + " · Esc to return to editing"
            );
    }

    internal void EndWalkthrough()
    {
        try
        {
            _mapScene?.Dispose();
        }
        finally
        {
            _mapScene = null;
            _ghostRevision = "";
            _walking = false;
            _walkLayout = null;
            if (_returnPosition.HasValue && _player)
            {
                if (ClearPosition(_returnPosition.Value, _player!))
                {
                    _player!.Teleport(_returnPosition.Value);
                    _player.Rotation = _returnFacing;
                }
                else
                    _notice = "Preview restored. Return position is obstructed; player remains here.";
            }
            _returnPosition = null;
            if (_session != null)
                _session.Hold = false;
            foreach (var panel in _walkHidden)
                if (panel)
                    panel.SetActive(true);
            _walkHidden.Clear();
            if (_view?.Valid == true)
                _view.Get<Transform>("Workspace").gameObject.SetActive(true);
        }
    }

    private static SeasonZone ToZone(MapVolume v) =>
        new()
        {
            Id = v.Id,
            Position = v.Position,
            Rotation = v.Rotation,
            Size = v.Size,
            Radius = v.Radius,
            Shape = v.Shape,
        };
}
