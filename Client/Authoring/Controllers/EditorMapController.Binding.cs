using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorMapController
{
    internal void Bind(RaidEditorView view)
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
                        _context.Notice = e.Message;
                        Plugin.Error(e);
                    }
                }
            );
        Button(
            "MapNew",
            () =>
            {
                if (!EditorMode.Ready || _context.Walking || _context.Session?.Definition == null)
                    return;
                _context.LayoutId = EditorMapRecords.NewId();
                _context.SelectionId = _context.LayoutId;
                _context.Session.Edit(s =>
                    s.MapLayouts.Add(new MapLayout { Id = _context.LayoutId, Location = _context.Session.Location })
                );
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
                    l.Start = _context.CapturePoint("Player start");
                    _context.SelectionId = l.Start.Id;
                })
        );
        Button(
            "MapCheckpoint",
            () =>
                MapEdit(l =>
                {
                    var v = _context.CaptureVolume("Checkpoint " + (l.Checkpoints.Count + 1));
                    PlayerRoute.Insert(l, _context.SelectionId, v);
                    _context.SelectionId = v.Id;
                })
        );
        Button(
            "MapExit",
            () =>
                MapEdit(l =>
                {
                    l.Exit = _context.CaptureVolume(_context.MissionContent ? "Exit" : "Extract");
                    _context.SelectionId = l.Exit.Id;
                })
        );
        Button(
            "MapBarrier",
            () =>
            {
                var v = _context.CaptureVolume("Barrier");
                // CapturePoint lands on the floor. Keep a new barrier above
                // that floor so its full collider participates in preview
                // and walkthrough collision checks.
                v.Position.Y += v.Shape == "Sphere" ? v.Radius : v.Size.Y / 2;
                _context.Picked = null;
                _context.SceneSelectionPose = null;
                _context.SceneSelectionError = "";
                _context.SceneTab = "Changes";
                MapEdit(l =>
                {
                    l.Barriers.Add(v);
                    _context.SelectionId = v.Id;
                });
            }
        );
        Button(
            "MapShape",
            () =>
                MapEdit(l =>
                {
                    if (MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _context.SelectionId) is MapVolume v)
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
                    if (!_context.Picked)
                        throw new InvalidOperationException("Pick the replacement scenery first.");
                    var edit = l.Objects.AsValueEnumerable().FirstOrDefault(p => p.Id == _context.SelectionId);
                    if (edit != null)
                        edit.Target = (_context.MapScene ??= new()).CaptureOriginal(_context.Picked!);
                    else if (l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _context.SelectionId) is { } door)
                        door.Target = MapSceneAdapter.Capture(_context.Picked!, true);
                })
        );
        Button(
            "MapAtPlayer",
            () =>
            {
                var placement = _context.CapturePoint("");
                _context.EditPoint(point =>
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
                _context.CameraRotation = Quaternion.Euler(25, point.Rotation.Y, 0);
                _context.CameraPosition = center - _context.CameraRotation * Vector3.forward * distance;
            }
        );
        Button("MapEarlier", () => ReorderCheckpoint(-1));
        Button("MapLater", () => ReorderCheckpoint(1));
        Button(
            "MapWalkStart",
            () =>
            {
                _context.WalkFromStart = !_context.WalkFromStart;
                _context.Refresh();
            }
        );
        Button("EditorWalk", _context.RequestWalkthrough);
        Button(
            "EditorReset",
            () =>
            {
                _context.EndWalkthrough();
                _context.Open();
            }
        );
        Button("EditorUnload", () => EditorMode.Instance.UnloadMap());
        view.Input(
            "MapName",
            text =>
            {
                if (_context.SceneWorkspace && (MapDoor != null || PickedDoor))
                {
                    EditDoor(d => d.Name = text.Trim());
                    return;
                }
                if (_context.SceneWorkspace && _context.ScenePoint != null)
                {
                    _context.EditPoint(point => point.Name = text.Trim());
                    return;
                }
                MapEdit(l =>
                {
                    var point = MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _context.SelectionId);
                    var door = l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _context.SelectionId);
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
                        _context.Number(
                            value,
                            number =>
                            {
                                if (_context.SceneWorkspace)
                                {
                                    if (field == "Size" && number <= 0)
                                        return;
                                    _context.EditTransformProperty(field, point => SceneSelectionEdit.SetAxis(point, field, axis, number));
                                    return;
                                }
                                MapEdit(l =>
                                {
                                    var point = MapLayoutRules
                                        .Points(l)
                                        .AsValueEnumerable()
                                        .FirstOrDefault(p => p.Id == _context.SelectionId);
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
}
