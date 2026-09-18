using System.Threading;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;
using Button = WTT.Campaigns.Client.Authoring.Views.EditorButton;
using InputField = WTT.Campaigns.Client.Authoring.Views.EditorInput;
using Text = WTT.Campaigns.Client.Authoring.Views.EditorLabel;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private string _layoutId = "",
        _ghostRevision = "";
    private bool _walking,
        _walkFromStart = true;
    private int _checkpoint;
    private Vector3? _returnPosition;
    private Vector2 _returnFacing;
    private Vector3? _walkCameraPosition;
    private Quaternion _walkCameraRotation;
    private string _walkCameraRaid = "";

    private void RestoreWalkCamera()
    {
        if (_walkCameraPosition.HasValue && _walkCameraRaid == _session?.RaidId)
        {
            _flyPosition = _walkCameraPosition.Value;
            _flyRotation = _walkCameraRotation;
        }
        _walkCameraPosition = null;
        _walkCameraRaid = "";
    }

    private MapSceneAdapter? _mapScene;
    private MapLayout? _walkLayout;
    private bool MapWorkspace => _mode == "Layouts" || _mode == "Routes";
    private MapLayout? Layout => _session?.Definition?.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == _layoutId);
    private SpatialCapture? MapPoint =>
        Layout == null ? null : MapLayoutRules.Points(Layout).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
    private MapDoorEdit? MapDoor => Layout?.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _selected);

    private static string MapId() => Guid.NewGuid().ToString("N").Substring(0, 24);

    private void MapEdit(Action<MapLayout> edit)
    {
        if (!EditorMode.Ready || _walking || AiPreviewBusy || Layout == null)
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
        Button("MapCopy", DuplicateMapRecord);
        Button("MapNormalRaid", () => MapEdit(l => l.ApplyInNormalRaids = !l.ApplyInNormalRaids));
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
                    PlayerRoute.Insert(l, _selected, v);
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
            {
                var v = CaptureVolume("Barrier");
                // CapturePoint lands on the floor. Keep a new barrier above
                // that floor so its full collider participates in preview
                // and walkthrough collision checks.
                v.Position.Y += v.Shape == "Sphere" ? v.Radius : v.Size.Y / 2;
                _picked = null;
                _sceneSelectionPose = null;
                _sceneSelectionError = "";
                _sceneTab = "Changes";
                MapEdit(l =>
                {
                    l.Barriers.Add(v);
                    _selected = v.Id;
                });
            }
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
                        edit.Target = (_mapScene ??= new()).CaptureOriginal(_picked!);
                    else if (l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _selected) is { } door)
                        door.Target = MapSceneAdapter.Capture(_picked!, true);
                })
        );
        Button(
            "MapAtPlayer",
            () =>
            {
                var placement = CapturePoint("");
                EditPoint(point =>
                {
                    point.Position = placement.Position;
                    point.Rotation = placement.Rotation;
                    point.Scene = placement.Scene;
                });
            }
        );
        Button(
            "RouteFrame",
            () =>
            {
                if (MapPoint is not { } point)
                    return;
                var center = ZoneRuntime.Vector(point.Position) + Vector3.up;
                var distance = point is MapVolume volume ? Mathf.Max(5, ZoneRuntime.Vector(volume.Size).magnitude * 1.5f) : 5;
                _flyRotation = Quaternion.Euler(25, point.Rotation.Y, 0);
                _flyPosition = center - _flyRotation * Vector3.forward * distance;
            }
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
        Button("EditorWalk", RequestWalkthrough);
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
            {
                if (SceneWorkspace && (MapDoor != null || PickedDoor))
                {
                    EditDoor(d => d.Name = text.Trim());
                    return;
                }
                if (SceneWorkspace && ScenePoint != null)
                {
                    EditPoint(point => point.Name = text.Trim());
                    return;
                }
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
                });
            }
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
                            {
                                if (SceneWorkspace)
                                {
                                    if (field == "Size" && number <= 0)
                                        return;
                                    EditTransformProperty(field, point => SceneSelectionEdit.SetAxis(point, field, axis, number));
                                    return;
                                }
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
                                });
                            }
                        )
                );
            }
    }

    private SpatialCapture CapturePoint(string name)
    {
        // The player stays at the raid spawn while the author flies the editor camera.
        // Capture the floor directly below that camera, never the distant player body.
        if (!_open || !_camera || !TryRouteFloor(_flyPosition, 20, out var floor))
            throw new InvalidOperationException("Move the editor camera above a floor within 20 metres to place a marker.");
        return new()
        {
            Id = MapId(),
            Name = name,
            Location = _session!.Location,
            Scene = floor.transform.gameObject.scene.name,
            Position = ZoneRuntime.Vector(floor.point),
            Rotation = new SpatialVector { Y = _flyRotation.eulerAngles.y },
        };
    }

    private static bool TryRouteFloor(Vector3 origin, float distance, out RaycastHit floor)
    {
        floor = Physics
            .RaycastAll(
                origin,
                Vector3.down,
                distance,
                Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Triggers")),
                QueryTriggerInteraction.Ignore
            )
            .AsValueEnumerable()
            .Where(hit => hit.collider && !hit.collider.GetComponentInParent<EFT.Player>())
            .OrderBy(hit => hit.distance)
            .FirstOrDefault();
        return floor.collider && floor.normal.y >= .5f;
    }

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
        if (operation == "Copy")
        {
            var error = MapSceneAdapter.Supported(_picked, copy: true);
            if (error.Length > 0)
                throw new InvalidOperationException(error);
        }
        var target = (_mapScene ??= new()).CaptureOriginal(_picked!);
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
        if (!EditorMode.Ready || _walking || _mode == "Routes" && _selected == _layoutId)
            return;
        if (_selected == _layoutId && Layout != null)
        {
            var copy = RaidEditorSession.Copy(Layout);
            var copiedIds = MapLayoutRules.OwnedIds(copy).AsValueEnumerable().ToDictionary(id => id, _ => MapId());
            WTT.Campaigns.Shared.Serialization.ModelGraph.Rewrite(
                copy,
                value => copiedIds.TryGetValue(value, out var fresh) ? fresh : value
            );
            copy.Name += " copy";
            var sourceLayoutId = _layoutId;
            _session!.Edit(s =>
            {
                s.MapLayouts.Add(copy);
                s.Zones.AddRange(ZoneLayoutRules.CopyOwnedZones(s, sourceLayoutId, copy.Id));
            });
            _layoutId = _selected = copy.Id;
        }
        else
            MapEdit(l =>
            {
                var point = MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _selected);
                if (point is MapLootPlacement loot)
                {
                    var copy = RaidEditorSession.Copy(loot);
                    copy.Id = MapId();
                    copy.Items = FreshItems(copy.Items);
                    l.Loot.Add(copy);
                    _selected = copy.Id;
                }
                else if (point is MapDoorEdit { PlaceNew: true } placedDoor)
                {
                    var copy = RaidEditorSession.Copy(placedDoor);
                    copy.Id = MapId();
                    copy.Name += " copy";
                    l.Doors.Add(copy);
                    _selected = copy.Id;
                }
                else if (point is MapObjectEdit assetEdit && (assetEdit.Target.IsAsset || SceneAssetRules.IsContainer(assetEdit)))
                {
                    var copy = RaidEditorSession.Copy(assetEdit);
                    copy.Id = MapId();
                    copy.Name += " copy";
                    l.Objects.Add(copy);
                    _selected = copy.Id;
                }
                else if (point is MapObjectEdit edit && edit.Target.Kind == "Prop")
                {
                    var target = _mapScene?.OriginalFor(edit.Target);
                    var error = MapSceneAdapter.Supported(target, copy: true);
                    if (error.Length > 0)
                        throw new InvalidOperationException(error);
                    var copy = RaidEditorSession.Copy(edit);
                    copy.Id = MapId();
                    copy.Operation = "Copy";
                    l.Objects.Add(copy);
                    _selected = copy.Id;
                }
                else if (point is MapVolume volume && (l.Barriers.Contains(volume) || l.Checkpoints.Contains(volume)))
                {
                    var copy = RaidEditorSession.Copy(volume);
                    copy.Id = MapId();
                    if (l.Barriers.Contains(volume))
                        l.Barriers.Add(copy);
                    else
                        PlayerRoute.Insert(l, volume.Id, copy);
                    _selected = copy.Id;
                }
            });
    }

    private void DeleteMapRecord()
    {
        if (!EditorMode.Ready || _walking || _mode == "Routes" && _selected == _layoutId)
            return;
        if (_selected == _layoutId)
        {
            if (!TryDeleteLayoutZones(_layoutId, out var error))
            {
                _notice = error;
                Refresh();
                return;
            }
            _layoutId = _selected = "";
        }
        else
            MapEdit(l =>
            {
                l.Objects.RemoveAll(p => p.Id == _selected);
                l.Loot.RemoveAll(p => p.Id == _selected);
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
            _rows.Add((layout.Id, "LAYOUT · " + layout.Name + (layout.ApplyInNormalRaids ? " · NORMAL RAIDS" : "")));
            if (layout.Id != _layoutId)
                continue;
            if (_mode != "Routes")
                continue;
            if (layout.Start != null)
                _rows.Add((layout.Start.Id, "START · " + layout.Start.Name));
            for (var i = 0; i < layout.Checkpoints.Count; i++)
                _rows.Add((layout.Checkpoints[i].Id, $"{i + 1}. {layout.Checkpoints[i].Name}"));
            if (layout.Exit != null)
                _rows.Add((layout.Exit.Id, "EXIT · " + layout.Exit.Name));
        }
    }

    private void RefreshMaps(bool geometry)
    {
        var view = _view!;
        var maps = (MapWorkspace || SceneWorkspace) && EditorMode.Ready;
        // Inspector visibility is owned by the workspace presenter.
        view.Get<Button>("Layouts").interactable = EditorMode.Ready;
        view.Get<Button>("Routes").interactable = EditorMode.Ready;

        view.Visible("EditorMapToolbar", EditorMode.Ready);
        if (EditorMode.Ready)
            view.Text("Request", "EDITOR MODE · Gameplay and progression disabled");
        if (EditorMode.Ready && !_walking && _walkAssetLifetime == null)
        {
            _mapScene ??= new();
            _mapScene.Reconcile(Layout, _drag?.Transient == true ? _sceneSelectionPose : null);
            if (
                _drag is { Centered: true } drag
                && drag.AnchorTarget
                && _tool != "Move"
                && Selected is { } dragged
                && _mapScene.TargetErrors.Count == 0
            )
            {
                var position = WTT.Campaigns.UI.Controls.SceneSelectionGeometry.PositionForAnchor(
                    drag.AnchorTarget!,
                    drag.LocalAnchor,
                    drag.Anchor
                );
                if (drag.AnchorTarget!.position != position)
                {
                    drag.AnchorTarget.position = position;
                    _mapScene.RefreshVisuals(drag.AnchorTarget);
                }
                dragged.Position = ZoneRuntime.Vector(position);
            }
            _selectionBoundsFrame = -1;
            if (SceneWorkspace && _picked && MapPoint == null && _sceneSelectionPose == null)
            {
                var error = MapSceneAdapter.Supported(_picked);
                SetSceneSelectionPose(_picked!, error.Length == 0 ? _mapScene.CaptureOriginal(_picked!) : null, error);
            }
        }
        if (!maps)
            return;
        view.Value("MapName", MapPoint?.Name ?? MapDoor?.Name ?? Layout?.Name ?? "");
        view.Checked("MapNormalRaid", Layout?.ApplyInNormalRaids == true);
        view.Get<Button>("MapNormalRaid").interactable = Layout != null && !_walking && !AiPreviewBusy;
        if (!SceneWorkspace)
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
        view.Checked("MapWalkStart", _walkFromStart);
        if (_mode == "Routes")
        {
            var index = Layout?.Checkpoints.FindIndex(p => p.Id == _selected) ?? -1;
            view.Text(
                "RouteGuide",
                Layout == null
                    ? "Choose a layout here, or create one in Layouts."
                    : "PLAYER ROUTE · "
                        + Layout.Checkpoints.Count
                        + " checkpoints\n"
                        + (
                            index >= 0
                                ? $"Checkpoint {index + 1} / {Layout.Checkpoints.Count} · inserts next"
                                : "Set start → add checkpoints → set exit."
                        )
                        + "\nStart green · Checkpoint amber · End red"
                        + "\nPlaced on floor beneath camera."
            );
        }
        var errors =
            Layout == null
                ? new List<string>
                {
                    _mode == "Routes"
                        ? "Create a layout in Layouts, then select it here."
                        : "Create or select a layout. Use Routes for player waypoints.",
                }
                : MapLayoutRules.Errors(Layout, _mode == "Routes");
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
        if (_mode == "Layouts")
            errors.AddRange(MapLayerRules.Errors(_session!.Definition!.MapLayouts));
        view.Text("MapDetails", (_picked ? "Picked: " + _picked!.name + "\n" : "") + errors.AsValueEnumerable().Take(3).JoinToString("\n"));
    }

    private static bool ClearPosition(Vector3 position, EFT.Player player)
    {
        var colliders = Physics.OverlapCapsule(
            position + Vector3.up * .4f,
            position + Vector3.up * 1.45f,
            .3f,
            Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Triggers")),
            QueryTriggerInteraction.Ignore
        );
        return !colliders.AsValueEnumerable().Any(c => c && c.GetComponentInParent<EFT.Player>() != player)
            && TryRouteFloor(position + Vector3.up * .15f, 1.5f, out _);
    }

    private bool _walkRequested;

    private void RequestWalkthrough()
    {
        if (_session?.Busy == true || _session?.Dirty == true)
        {
            _walkRequested = true;
            _notice = "Starting walkthrough after synchronization…";
            Refresh(false);
            return;
        }
        BeginWalkthrough();
    }

    private async void BeginWalkthrough()
    {
        if (
            !EditorMode.Ready
            || _walking
            || _walkAssetLifetime != null
            || AiPreviewBusy
            || Layout == null
            || _session?.Conflict != null
            || _session?.Busy == true
            || _session?.Dirty == true
        )
            return;
        _mapScene ??= new();
        _walkAssetLifetime?.Cancel();
        _walkAssetLifetime = new CancellationTokenSource();
        var walkLifetime = _walkAssetLifetime;
        _session!.Previewing = _session.Hold = true;
        _notice = "Preparing walkthrough scenery and containers�";
        try
        {
            _walkLayout = RaidEditorSession.Copy(Layout);
            await _mapScene.ApplyAsync(_walkLayout, true, walkLifetime.Token, runtime: true);
            _walkAssetLoot = new WTT.Campaigns.Client.Missions.MissionLoot();
            await _walkAssetLoot.ApplyAsync(_walkLayout, Guid.NewGuid().ToString("N"), walkLifetime.Token);
            walkLifetime.Token.ThrowIfCancellationRequested();
            Physics.SyncTransforms();
            Vector3? destination = null;
            if (_walkFromStart)
            {
                destination = ZoneRuntime.Vector(_walkLayout.Start!.Position);
                if (!ClearPosition(destination.Value, _player!))
                    throw new InvalidOperationException("The start marker needs clear standing room and a floor.");
                _returnPosition = _player!.Transform.position;
                _returnFacing = _player.Rotation;
            }
            _walking = true;
            _checkpoint = 0;
            _session!.Previewing = _session.Hold = true;
            _walkCameraPosition = _flyPosition;
            _walkCameraRotation = _flyRotation;
            _walkCameraRaid = _session.RaidId;
            Close();
            // Restore the native camera before teleporting; Close restores its saved world pose.
            if (destination.HasValue)
            {
                _player!.Teleport(destination.Value);
                _player.Rotation = new Vector2(_walkLayout.Start!.Rotation.Y, _walkLayout.Start.Rotation.X);
            }
            _view!.SetVisible(true);
            _view.Windows.SetWalkthrough(true);
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        catch (Exception e)
        {
            if (ReferenceEquals(_walkAssetLifetime, walkLifetime))
            {
                EndWalkthrough();
                _notice = e.Message;
                Refresh(false);
            }
        }
    }

    private CancellationTokenSource? _walkAssetLifetime;
    private WTT.Campaigns.Client.Missions.MissionLoot? _walkAssetLoot;

    private bool UpdateWalkthrough()
    {
        if (!_walking)
            return false;
        if (
            !EditorMode.Ready
            || _session?.Grant.Length == 0
            || _session?.Conflict != null
            || !_player
            || Input.GetKeyDown(KeyCode.Escape)
            || _shortcut.Value.IsDown()
        )
        {
            EndWalkthrough(returnToEditor: true);
            return true;
        }
        var route = _walkLayout!;
        var target = _checkpoint < route.Checkpoints.Count ? route.Checkpoints[_checkpoint] : route.Exit;
        if (target != null && _checkpoint <= route.Checkpoints.Count && Inside(ToZone(target), _player!.Transform.position))
            _checkpoint++;
        if (_view?.Valid == true)
            _view.Text("EditorWalkStatus", PlayerRoute.Progress(route, _checkpoint) + " · Esc to return to editing");
        return false;
    }

    internal void EndWalkthrough(bool returnToEditor = false)
    {
        _walkAssetLifetime?.Cancel();
        _walkAssetLifetime?.Dispose();
        _walkAssetLifetime = null;
        _walkAssetLoot?.Dispose();
        _walkAssetLoot = null;
        if (AiPreviewBusy)
            EndAiPreview(false);
        var transition = returnToEditor ? System.Diagnostics.Stopwatch.StartNew() : null;
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
                Physics.SyncTransforms();
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
                _session.Previewing = _session.Hold = _aiCleanupFailed;
            if (_view?.Valid == true)
                _view.Windows.SetWalkthrough(false);
        }
        // Reclaim the camera before this frame renders. Update consumes the exit
        // input, so waiting for automatic opening exposes the native player pose.
        // Teardown callers leave returnToEditor false and must never reopen UI.
        if (returnToEditor && EditorMode.Ready && AuthoringEnabled && _session?.Definition != null)
        {
            var cleanupMilliseconds = transition!.Elapsed.TotalMilliseconds;
            Open();
            Plugin.LogInfo(
                $"Walkthrough return: cleanup {cleanupMilliseconds:0.0} ms, editor reopening "
                    + $"{transition.Elapsed.TotalMilliseconds - cleanupMilliseconds:0.0} ms."
            );
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
