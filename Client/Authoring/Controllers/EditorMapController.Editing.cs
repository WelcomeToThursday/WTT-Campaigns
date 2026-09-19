using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorMapController
{
    internal SpatialCapture? MapPoint =>
        _context.Layout == null
            ? null
            : MapLayoutRules.Points(_context.Layout).AsValueEnumerable().FirstOrDefault(p => p.Id == _context.SelectionId);

    internal MapDoorEdit? MapDoor => _context.Layout?.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _context.SelectionId);

    internal void MapEdit(Action<MapLayout> edit)
    {
        if (!EditorMode.Ready || _context.Walking || _context.AiPreviewBusy || _context.Layout == null)
            return;
        _context.Session!.Edit(s => edit(s.MapLayouts.AsValueEnumerable().Single(l => l.Id == _context.LayoutId)));
        _context.GhostRevision = "";
        _context.Refresh();
    }

    internal void CaptureMapObject(string operation)
    {
        if (!_context.Picked)
            throw new InvalidOperationException("Use Pick scene object first.");
        if (operation == "Copy")
        {
            var error = MapSceneAdapter.Supported(_context.Picked, copy: true);
            if (error.Length > 0)
                throw new InvalidOperationException(error);
        }
        var target = (_context.MapScene ??= new()).CaptureOriginal(_context.Picked!);
        MapEdit(l =>
        {
            var edit = new MapObjectEdit
            {
                Id = EditorMapRecords.NewId(),
                Name = _context.Picked!.name,
                Location = l.Location,
                Scene = target.Scene,
                Target = target,
                Operation = operation,
                Position = ZoneRuntime.Vector(_context.Picked.position),
                Rotation = ZoneRuntime.Vector(_context.Picked.eulerAngles),
                Scale = ZoneRuntime.Vector(_context.Picked.lossyScale),
            };
            if (operation != "Copy")
                l.Objects.RemoveAll(o => o.Operation != "Copy" && o.Target.Scene == target.Scene && o.Target.Path == target.Path);
            l.Objects.Add(edit);
            _context.SelectionId = edit.Id;
        });
    }

    private void ChangeMapDoor()
    {
        MapEdit(l =>
        {
            var door = l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _context.SelectionId);
            if (door == null)
            {
                if (!_context.Picked)
                    throw new InvalidOperationException("Pick a native door first.");
                var target = MapSceneAdapter.Capture(_context.Picked!, true);
                door = l.Doors.AsValueEnumerable().FirstOrDefault(d => d.Target.Path == target.Path && d.Target.Scene == target.Scene);
                if (door == null)
                {
                    door = new MapDoorEdit
                    {
                        Id = EditorMapRecords.NewId(),
                        Name = _context.Picked!.name,
                        Target = target,
                    };
                    l.Doors.Add(door);
                }
                _context.SelectionId = door.Id;
            }
            var states = new[] { "Unchanged", "Open", "Shut", "Locked" };
            door.State = states[(Array.IndexOf(states, door.State) + 1) % states.Length];
        });
    }

    private void ReorderCheckpoint(int direction) =>
        MapEdit(layout => EditorMapRecords.ReorderCheckpoint(layout, _context.SelectionId, direction));

    internal void DuplicateMapRecord()
    {
        if (!EditorMode.Ready || _context.Walking || _context.ToolId == "Routes" && _context.SelectionId == _context.LayoutId)
            return;
        if (_context.SelectionId == _context.LayoutId && _context.Layout != null)
        {
            var copy = EditorMapRecords.DuplicateLayout(_context.Layout);
            var sourceLayoutId = _context.LayoutId;
            _context.Session!.Edit(s =>
            {
                s.MapLayouts.Add(copy);
                s.Zones.AddRange(ZoneLayoutRules.CopyOwnedZones(s, sourceLayoutId, copy.Id));
            });
            _context.LayoutId = _context.SelectionId = copy.Id;
        }
        else
            MapEdit(l =>
            {
                var point = MapLayoutRules.Points(l).AsValueEnumerable().FirstOrDefault(p => p.Id == _context.SelectionId);
                if (point is MapLootPlacement loot)
                {
                    var copy = RaidEditorSession.Copy(loot);
                    copy.Id = EditorMapRecords.NewId();
                    copy.Items = EditorMapRecords.FreshItems(copy.Items);
                    l.Loot.Add(copy);
                    _context.SelectionId = copy.Id;
                }
                else if (point is MapDoorEdit { PlaceNew: true } placedDoor)
                {
                    var copy = RaidEditorSession.Copy(placedDoor);
                    copy.Id = EditorMapRecords.NewId();
                    copy.Name += " copy";
                    l.Doors.Add(copy);
                    _context.SelectionId = copy.Id;
                }
                else if (point is MapObjectEdit assetEdit && (assetEdit.Target.IsAsset || SceneAssetRules.IsContainer(assetEdit)))
                {
                    var copy = RaidEditorSession.Copy(assetEdit);
                    copy.Id = EditorMapRecords.NewId();
                    copy.Name += " copy";
                    l.Objects.Add(copy);
                    _context.SelectionId = copy.Id;
                }
                else if (point is MapObjectEdit edit && edit.Target.Kind == "Prop")
                {
                    var target = _context.MapScene?.OriginalFor(edit.Target);
                    var error = MapSceneAdapter.Supported(target, copy: true);
                    if (error.Length > 0)
                        throw new InvalidOperationException(error);
                    var copy = RaidEditorSession.Copy(edit);
                    copy.Id = EditorMapRecords.NewId();
                    copy.Operation = "Copy";
                    l.Objects.Add(copy);
                    _context.SelectionId = copy.Id;
                }
                else if (point is MapVolume volume && (l.Barriers.Contains(volume) || l.Checkpoints.Contains(volume)))
                {
                    var copy = EditorMapRecords.DuplicateVolume(l, volume);
                    _context.SelectionId = copy.Id;
                }
            });
    }

    internal void DeleteMapRecord()
    {
        if (!EditorMode.Ready || _context.Walking || _context.ToolId == "Routes" && _context.SelectionId == _context.LayoutId)
            return;
        if (_context.SelectionId == _context.LayoutId)
        {
            if (!_context.TryDeleteLayoutZones(_context.LayoutId, out var error))
            {
                _context.Notice = error;
                _context.Refresh();
                return;
            }
            _context.LayoutId = _context.SelectionId = "";
        }
        else
            MapEdit(l =>
            {
                EditorMapRecords.Remove(l, _context.SelectionId);
                _context.SelectionId = l.Id;
            });
        _context.Refresh();
    }
}
