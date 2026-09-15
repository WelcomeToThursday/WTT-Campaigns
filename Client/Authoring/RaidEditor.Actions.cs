using System.Globalization;
using EFT.Ballistics;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Client.Story;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Shared.Story;
using WTT.Campaigns.UI.Controls;
using ZLinq;
using Button = WTT.Campaigns.Client.Authoring.Views.EditorButton;
using Dropdown = WTT.Campaigns.Client.Authoring.Views.EditorChoice;
using InputField = WTT.Campaigns.Client.Authoring.Views.EditorInput;
using Text = WTT.Campaigns.Client.Authoring.Views.EditorLabel;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private StoryRaidBinding? Binding
    {
        get
        {
            return _session
                ?.Definition?.Story?.RaidBindings.AsValueEnumerable()
                .FirstOrDefault(b => b.Id == (_task?.TargetKind == "Binding" ? _task.TargetId : _bindingTarget));
        }
    }

    private string _bindingTarget = "";
    private readonly Dictionary<string, (string Selection, int Page)> _moduleSelection = new();
    private readonly List<string> _zoneScopeIds = new();

    // Shared is an explicit choice; the other scope always follows the current
    // layout so changing layouts cannot create an invisible zone under an old
    // owner.
    private bool _zoneCreateShared;

    private RaidEditorView BuildView()
    {
        var view = new RaidEditorView();
        try
        {
            BindEnvironment(view);
            BindCameraControls(view);
            view.Button("CloseEditor", Close);
            void Button(string name, Action action)
            {
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
            }

            void Dropdown(string name, Action<int> action)
            {
                view.Dropdown(
                    name,
                    value =>
                    {
                        try
                        {
                            action(value);
                        }
                        catch (Exception e)
                        {
                            _notice = e.Message;
                            Plugin.Error(e);
                        }
                    }
                );
            }

            foreach (var mode in new[] { "Layouts", "Routes", "Zones", "Bindings", "Captures", "Scene", "AI" })
            {
                var value = mode;
                Button(
                    mode,
                    () =>
                    {
                        CancelPlacement();
                        _sceneRebindId = "";
                        _picking = false;
                        CancelDrag();
                        _moduleSelection[_mode] = (_selected, _page);
                        _mode = value;
                        var previous = _moduleSelection.GetValueOrDefault(value);
                        _selected = previous.Selection ?? "";
                        if (value == "Layouts" && _session?.Definition != null)
                        {
                            // Layouts owns layout records. Returning from Scene
                            // or Routes always follows the active layout.
                            _selected = _layoutId;
                        }
                        if (value == "Zones" && EditorMode.Ready && _session?.Definition != null)
                        {
                            if (!FilterZonesForLayout(_layoutId).AsValueEnumerable().Any(z => z.Id == _selected))
                                _selected = "";
                        }
                        _page = previous.Page;
                        Refresh();
                        view.Windows.BrowseCategory();
                    }
                );
            }
            for (var i = 0; i < 10; i++)
            {
                var binding = view.Get<Button>("Row" + i);
                Button("Row" + i, () => SelectRow(binding.Consume()));
            }
            view.BindTreeSelection(SelectRow);
            view.Get<InputField>("Search")
                .onValueChanged.AddListener(_ =>
                {
                    _page = 0;
                    Refresh(false);
                });
            Button(
                "Previous",
                () =>
                {
                    _page = Math.Max(0, _page - 1);
                    Refresh();
                }
            );
            Button(
                "Next",
                () =>
                {
                    if ((_page + 1) * 10 < LibraryTotal)
                    {
                        _page++;
                    }
                    Refresh();
                }
            );
            Button(
                "AddBox",
                () =>
                {
                    if (_mode == "Bindings")
                    {
                        AddBinding("Trigger");
                    }
                    else
                    {
                        AddZone("Box");
                    }
                }
            );
            Button(
                "AddSphere",
                () =>
                {
                    if (_mode == "Bindings")
                    {
                        AddBinding("Interact");
                    }
                    else
                    {
                        AddZone("Sphere");
                    }
                }
            );
            Dropdown("ZoneCreateScope", value => SetZoneScope(shared: value == 0, applyToSelection: false));
            Dropdown(
                "ZoneScope",
                value =>
                {
                    if (value >= 0 && value < _zoneScopeIds.Count)
                        SetSelectedZoneScope(_zoneScopeIds[value]);
                }
            );
            Button("Capture", () => Capture(false));
            Button(
                "Pick",
                () =>
                {
                    _picking = true;
                    _notice = "Click a scene object. Use Select parent to choose its binding target.";
                }
            );
            Button(
                "Undo",
                () =>
                {
                    CancelDrag();
                    _session?.Undo(false);
                }
            );
            Button(
                "Redo",
                () =>
                {
                    CancelDrag();
                    _session?.Undo(true);
                }
            );
            Button("Duplicate", Duplicate);
            Button("Delete", Delete);
            Button("AtFeet", () => Place(false));
            Button("AtAim", () => Place(true));
            Button(
                "Parent",
                () =>
                {
                    if (_picked?.parent)
                    {
                        _picked = _picked!.parent;
                    }
                    Refresh();
                }
            );
            Button("UseObject", BindTarget);
            foreach (var tool in new[] { "Move", "Rotate", "Scale" })
            {
                var value = tool;
                Button(
                    tool,
                    () =>
                    {
                        CancelDrag();
                        if (SceneWorkspace)
                            SceneTransform(value);
                        else
                            _tool = value;
                        Refresh();
                    }
                );
            }
            Button(
                "Snap",
                () =>
                {
                    _snap = !_snap;
                    Refresh();
                }
            );
            foreach (var use in new[] { "InZone", "VisitPlace", "LeaveItemAtLocation" })
            {
                var value = use;
                Button(
                    use,
                    () =>
                        EditPoint(point =>
                        {
                            if (point is SeasonZone zone)
                            {
                                if (!zone.Uses.Remove(value))
                                {
                                    zone.Uses.Add(value);
                                }
                            }
                        })
                );
            }
            view.Input(
                "Name",
                value =>
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return;
                    }
                    if (_mode == "Bindings" && Binding != null)
                    {
                        var id = Binding.Id;
                        _session?.Edit(s => s.Story!.RaidBindings.AsValueEnumerable().Single(b => b.Id == id).Name = value.Trim());
                    }
                    else if (_mode == "AI")
                    {
                        EditAiName(value);
                    }
                    else
                    {
                        EditPoint(point => point.Name = value.Trim());
                    }
                }
            );
            Button(
                "EventKind",
                () =>
                {
                    if (Binding == null)
                    {
                        return;
                    }
                    var id = Binding.Id;
                    _session?.Edit(s =>
                    {
                        var b = s.Story!.RaidBindings.AsValueEnumerable().Single(x => x.Id == id);
                        var kinds = new[] { "Trigger", "Interact", "Shoot", "Cinematic" };
                        b.Kind = kinds[(Array.IndexOf(kinds, b.Kind) + 1) % kinds.Length];
                        if (b.Kind is "Interact" or "Shoot")
                        {
                            b.ZoneId = "";
                        }
                    });
                }
            );
            foreach (var group in new[] { "Position", "Rotation", "Size" })
            {
                for (var i = 0; i < 3; i++)
                {
                    var property = group;
                    var axis = i;
                    view.Input(
                        group + "XYZ"[i],
                        value =>
                            Number(
                                value,
                                number =>
                                {
                                    if (_mode == "AI")
                                    {
                                        EditAiVector(property, axis, number);
                                        return;
                                    }
                                    EditPoint(point =>
                                    {
                                        var vector =
                                            property == "Position" ? point.Position
                                            : property == "Rotation" ? point.Rotation
                                            : (point as SeasonZone)?.Size;
                                        if (vector == null || property == "Size" && number <= 0)
                                        {
                                            return;
                                        }

                                        if (axis == 0)
                                        {
                                            vector.X = number;
                                        }
                                        else if (axis == 1)
                                        {
                                            vector.Y = number;
                                        }
                                        else
                                        {
                                            vector.Z = number;
                                        }
                                    });
                                }
                            )
                    );
                }
            }

            view.Input(
                "Radius",
                value =>
                    Number(
                        value,
                        number =>
                        {
                            if (number > 0)
                            {
                                if (_mode == "AI")
                                {
                                    EditAiRadius(number);
                                }
                                else
                                    EditPoint(p =>
                                    {
                                        if (p is SeasonZone z)
                                        {
                                            z.Radius = number;
                                        }
                                    });
                            }
                        }
                    )
            );
            Button("Complete", CompleteTask);
            Button(
                "Cancel",
                () =>
                {
                    CancelDrag();
                    if (_task != null)
                    {
                        _session?.TaskStatus(_task, "Cancelled");
                    }
                    else
                    {
                        Close();
                    }
                }
            );
            Button("KeepLocal", () => _session?.Resolve(true));
            Button("KeepRemote", () => _session?.Resolve(false));
            BindMapControls(view);
            BindSceneControls(view);
            BindAiControls(view);
            return view;
        }
        catch
        {
            view.Dispose();
            throw;
        }
    }

    private void Number(string text, Action<float> action)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && float.IsFinite(number))
        {
            action(number);
        }
        else
        {
            _notice = "Enter a finite number using a decimal point.";
        }
    }

    private void EditPoint(Action<SpatialCapture> action)
    {
        if (AiWorkspace)
        {
            var proxy = AiSelectedPoint();
            if (proxy == null)
                return;
            action(proxy);
            EditAiPoint(proxy);
            return;
        }
        if (SceneWorkspace && !CanTransformScene("Move"))
            return;
        if (SceneWorkspace && MapPoint == null)
        {
            CommitSceneSelection(action);
            return;
        }
        var id = _selected;
        _session?.Edit(s =>
        {
            var point =
                (MapWorkspace || SceneWorkspace)
                    ? s.MapLayouts.AsValueEnumerable().SelectMany(MapLayoutRules.Points).FirstOrDefault(p => p.Id == id)
                    : s.Zones.AsValueEnumerable().Cast<SpatialCapture>().Concat(s.Captures).FirstOrDefault(p => p.Id == id);
            if (point != null)
            {
                action(point);
            }
        });
    }

    private void AddBinding(string kind)
    {
        if (_session?.Definition == null)
        {
            return;
        }

        var id = Guid.NewGuid().ToString("N").Substring(0, 24);
        _session.Edit(s =>
        {
            s.Story ??= new();
            s.Story.RaidBindings.Add(
                new()
                {
                    Id = id,
                    Name = "New " + kind.ToLowerInvariant() + " event",
                    Kind = kind,
                    Location = _session.Location,
                }
            );
        });
        _bindingTarget = id;
        _selected = "";
        Refresh();
    }

    private void AddZone(string shape)
    {
        if (_session?.Definition == null)
        {
            _notice = "Connect a draft in the web editor first.";
            return;
        }
        var id = Guid.NewGuid().ToString("N").Substring(0, 24);
        var position = Aim(out var scene) ?? _player!.Transform.position;
        if (scene.Length == 0)
        {
            scene = PlayerScene();
        }

        var layoutId = !_zoneCreateShared && EditorMode.Ready ? _layoutId : "";
        if (!_zoneCreateShared && EditorMode.Ready && Layout == null)
        {
            _notice = "Select a layout in Layouts before creating a layout-owned zone.";
            Refresh();
            return;
        }
        _session.Edit(s =>
            s.Zones.Add(
                new SeasonZone
                {
                    Id = id,
                    Name = "New " + shape.ToLowerInvariant() + " zone",
                    Shape = shape,
                    Location = _session.Location,
                    Scene = scene,
                    Position = ZoneRuntime.Vector(position),
                    LayoutId = layoutId,
                }
            )
        );
        _selected = id;
        _mode = "Zones";
        Refresh();
    }

    private void SetZoneScope(bool shared, bool applyToSelection)
    {
        if (_session?.Definition == null)
            return;
        if (!shared && (!EditorMode.Ready || Layout == null))
        {
            _notice = "Select a layout in Layouts before assigning layout ownership.";
            Refresh();
            return;
        }

        if (applyToSelection && Selected is SeasonZone zone)
        {
            var layoutId = shared ? "" : _layoutId;
            if (!SetZoneLayout(zone.Id, layoutId, out var error))
            {
                _notice = error;
                Refresh();
                return;
            }
        }

        _zoneCreateShared = shared;
        _notice = shared
            ? "New zones will be Shared across layouts."
            : "New zones will belong to " + (Layout?.Name ?? "the selected layout") + ".";
        Refresh();
    }

    private void SetSelectedZoneScope(string layoutId)
    {
        if (Selected is not SeasonZone zone)
            return;

        if (!SetZoneLayout(zone.Id, layoutId, out var error))
        {
            _notice = error;
            Refresh();
            return;
        }

        if (!string.IsNullOrEmpty(layoutId))
            _layoutId = layoutId;
        _libraryKey = "";
        var owner = string.IsNullOrEmpty(layoutId)
            ? "Shared"
            : _session?.Definition?.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == layoutId)?.Name ?? layoutId;
        _notice = "Zone scope changed to " + owner + ".";
        Refresh();
    }

    private Vector3? Aim(out string scene)
    {
        scene = "";
        if (!_camera)
        {
            return null;
        }

        if (
            !Physics.Raycast(
                _flyPosition,
                _flyRotation * Vector3.forward,
                out var hit,
                1000,
                ~(1 << LayerMask.NameToLayer("Triggers")),
                QueryTriggerInteraction.Ignore
            )
        )
        {
            return null;
        }

        scene = hit.transform.gameObject.scene.name;
        return hit.point;
    }

    private string PlayerScene()
    {
        if (
            _player
            && Physics.Raycast(
                _player!.Transform.position + Vector3.up,
                Vector3.down,
                out var hit,
                20,
                ~(1 << LayerMask.NameToLayer("Triggers")),
                QueryTriggerInteraction.Ignore
            )
        )
        {
            return hit.transform.gameObject.scene.name;
        }

        return _player!.gameObject.scene.name;
    }

    private void Place(bool aim)
    {
        if (AiWorkspace)
        {
            if (!TryAiPlacement(out var aiPosition, out var aiScene))
                return;
            var selected = AiSelectedPoint();
            if (selected == null)
                return;
            selected.Position = ZoneRuntime.Vector(aiPosition);
            selected.Scene = aiScene;
            EditAiPoint(selected);
            return;
        }
        var scene = PlayerScene();
        var position = aim ? Aim(out scene) : _player?.Transform.position;
        if (position == null)
        {
            _notice = "Aim at scene geometry first.";
            return;
        }
        EditPoint(point =>
        {
            point.Position = ZoneRuntime.Vector(position.Value);
            point.Scene = scene;
        });
    }

    private void Capture(bool sceneObject)
    {
        if (_session?.Definition == null || !_camera)
        {
            return;
        }

        if (sceneObject && !_picked)
        {
            _notice = "Pick a scene object first.";
            return;
        }
        var id = Selected is not SeasonZone && Selected != null ? Selected.Id : Guid.NewGuid().ToString("N").Substring(0, 24);
        var point = new SpatialCapture
        {
            Id = id,
            Name = sceneObject ? _picked!.name : "Camera transform",
            Location = _session.Location,
            Scene = sceneObject ? _picked!.gameObject.scene.name : PlayerScene(),
            Position = ZoneRuntime.Vector(sceneObject ? _picked!.position : _flyPosition),
            Rotation = ZoneRuntime.Vector(sceneObject ? _picked!.eulerAngles : _flyRotation.eulerAngles),
            ObjectPath = sceneObject ? PickedPath : "",
        };
        _session.Edit(s =>
        {
            s.Captures.RemoveAll(c => c.Id == id);
            s.Captures.Add(point);
        });
        _selected = id;
        _mode = "Captures";
        Refresh();
    }

    private void Duplicate()
    {
        if (AiWorkspace)
        {
            DuplicateAiSelection();
            return;
        }
        if (MapWorkspace || SceneWorkspace)
        {
            DuplicateMapRecord();
            return;
        }
        if (_mode == "Bindings" && Binding != null)
        {
            var copied = RaidEditorSession.Copy(Binding);
            copied.Id = Guid.NewGuid().ToString("N").Substring(0, 24);
            copied.Name += " copy";
            _session?.Edit(s => s.Story!.RaidBindings.Add(copied));
            _bindingTarget = copied.Id;
            Refresh();
            return;
        }
        if (Selected is not { } point)
        {
            return;
        }

        var id = Guid.NewGuid().ToString("N").Substring(0, 24);
        _session!.Edit(s =>
        {
            if (point is SeasonZone zone)
            {
                var copy = RaidEditorSession.Copy(zone);
                copy.Id = id;
                copy.Name += " copy";
                s.Zones.Add(copy);
            }
            else
            {
                var copy = RaidEditorSession.Copy(point);
                copy.Id = id;
                copy.Name += " copy";
                s.Captures.Add(copy);
            }
        });
        _selected = id;
        Refresh();
    }

    private void Delete()
    {
        if (AiWorkspace)
        {
            DeleteAiSelection();
            Refresh();
            return;
        }
        if (MapWorkspace || SceneWorkspace)
        {
            DeleteMapRecord();
            return;
        }
        if (_session?.Definition == null)
        {
            return;
        }

        if (_mode == "Bindings" && Binding != null)
        {
            var id = Binding.Id;
            _session.Edit(s => s.Story!.RaidBindings.RemoveAll(b => b.Id == id));
            _bindingTarget = "";
            Refresh();
            return;
        }
        var uses = SpatialRules.Uses(_session.Definition, _selected).AsValueEnumerable().ToArray();
        if (uses.Length > 0)
        {
            _notice = "Reassign before deleting: " + string.Join(", ", uses);
            return;
        }
        _session.Edit(s =>
        {
            s.Zones.RemoveAll(z => z.Id == _selected);
            s.Captures.RemoveAll(c => c.Id == _selected);
        });
        _selected = "";
        Refresh();
    }

    private void SelectRow(string id)
    {
        if (id.Length == 0)
            return;
        CancelDrag();
        if (MapWorkspace)
        {
            if (_mode == "Routes" && _session?.Definition != null)
                _layoutId = EditorLibraryTrees.RouteOwner(_session.Definition.MapLayouts, _session.Location, id) ?? _layoutId;
            if (_session?.Definition?.MapLayouts.AsValueEnumerable().Any(l => l.Id == id) == true)
                _layoutId = id;
            _selected = id;
            if (EditorMode.Ready && MapPoint is MapObjectEdit or MapLootPlacement)
            {
                EnterSceneSelection();
                SelectSceneRow(id);
            }
        }
        else if (_mode == "AI")
        {
            _selected = id;
            _picked = null;
        }
        else if (_mode == "Scene")
        {
            if (SceneWorkspace)
                SelectSceneRow(id);
            else
                _picked = _sceneIndex.Entries[int.Parse(id, CultureInfo.InvariantCulture)].Target;
        }
        else if (_mode == "Bindings")
        {
            _bindingTarget = id;
            _selected = "";
            var binding = Binding;
            _picked = _sceneIndex.Unique(binding?.ObjectPath);
        }
        else
        {
            _selected = id;
            _picked = null;
            if (Selected?.ObjectPath is { Length: > 0 } path)
            {
                _picked = _sceneIndex.Unique(path);
            }
        }
        Refresh();
        _view!.Windows.ShowPanel("Inspector", true);
    }

    private string ObjectError()
    {
        if (!_picked)
        {
            return "Pick a scene target first.";
        }

        var path = PickedPath;
        if (!_sceneIndex.Complete || _sceneIndex.Limited)
            return SceneIndexStatus;
        if (_sceneIndex.Unique(path) != _picked)
        {
            return "This path is ambiguous. Select a uniquely named target.";
        }

        if (Binding?.Kind == "Shoot" && !_picked!.GetComponent<BallisticCollider>())
        {
            return "Shoot targets need a ballistic collider on the selected object.";
        }

        if (Binding?.Kind is "Trigger" or "Cinematic" && !_picked!.GetComponents<Collider>().AsValueEnumerable().Any(c => c.isTrigger))
        {
            return "This event needs an existing trigger collider or an authored zone.";
        }

        if (Binding?.Kind == "Interact" && !_picked!.GetComponentInChildren<Collider>())
        {
            return "Interact targets need a raycastable collider.";
        }

        if (Binding?.Kind == "Collectible")
        {
            return "Collectible events use an item template; configure it in the web editor.";
        }

        return "";
    }

    private void BindTarget()
    {
        if (_session?.Definition == null)
        {
            return;
        }

        if (Selected is SeasonZone zone && Binding != null)
        {
            if (Binding.Kind is not ("Trigger" or "Cinematic"))
            {
                _notice = "Only Trigger and Cinematic events accept zones.";
                return;
            }
            var id = Binding.Id;
            _session.Edit(s =>
            {
                var binding = s.Story!.RaidBindings.AsValueEnumerable().Single(b => b.Id == id);
                binding.ZoneId = zone.Id;
                binding.ObjectPath = "";
                binding.Location = zone.Location;
            });
            return;
        }
        var error = ObjectError();
        if (error.Length > 0)
        {
            _notice = error;
            return;
        }
        Capture(true);
        if (Binding == null || _task != null)
        {
            return;
        }

        var bindingId = Binding.Id;
        var path = PickedPath;
        _session.Edit(s =>
        {
            var binding = s.Story!.RaidBindings.AsValueEnumerable().Single(b => b.Id == bindingId);
            binding.ObjectPath = path;
            binding.ZoneId = "";
            binding.Location = _session.Location;
        });
    }

    private void CompleteTask()
    {
        if (_session == null || _task == null || _session.Busy || _session.Conflict != null)
        {
            return;
        }

        if (Selected == null)
        {
            _notice = "Select the captured record first.";
            return;
        }
        if (_task.Tool == "Zone" && Selected is not SeasonZone)
        {
            _notice = "This task needs a zone.";
            return;
        }
        if (_task.Tool == "Object" && (Selected.ObjectPath.Length == 0 || ObjectError().Length > 0))
        {
            _notice = ObjectError();
            return;
        }
        _session.TaskStatus(_task, "Completed", Selected.Id);
    }

    private void Refresh()
    {
        Refresh(true);
    }

    private string _libraryKey = "";

    private void Refresh(bool geometry)
    {
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.Presentation);
        if (_view?.Valid != true || !_open || _session == null)
        {
            return;
        }

        var view = _view;
        view.SetToolkitContext(ToolkitContext);
        view.Text(
            "Connection",
            (_session.Definition?.Name ?? "Waiting for a connected draft")
                + " / "
                + _session.Location
                + (EditorMode.Ready ? " / " + (Layout?.Name ?? "New layout") : "")
        );
        view.Text(
            "Request",
            _task == null ? "RAID CONTINUES Ã‚Â· Player remains in place" : "RAID CONTINUES Ã‚Â· " + _task.Tool + " capture requested"
        );
        view.Text("Status", _session.Status + (_notice.Length > 0 ? "\n" + _notice : ""));
        view.Conflict(_session);
        // Do not repurpose or hide a row between pointer-down and pointer-up.
        if (view.RowPressed)
        {
            _passiveState = "";
            return;
        }
        foreach (var mode in new[] { "Layouts", "Routes", "Zones", "Bindings", "Captures", "Scene", "AI" })
            view.Highlight(mode, _mode == mode);
        var search = view.Get<InputField>("Search").text;
        var treeMode = _mode == "AI" || EditorMode.Ready && (_mode == "Routes" || _mode == "Zones");
        var libraryKey =
            $"{_mode}|{_sceneTab}|{_sceneFilter}|{search}|{_layoutId}|{_session.ContentVersion}|{_sceneIndex.Count}|{_catalogGeneration}|{_catalogLoading}|{(RemoteCatalog ? _page : 0)}";
        if (_libraryKey != libraryKey)
        {
            _libraryKey = libraryKey;
            _rows.Clear();
            if (MapWorkspace && _session.Definition != null && !treeMode)
                MapRows();
            else if (SceneWorkspace)
                SceneRows(search);
            else if (_mode == "Scene")
            {
                foreach (var entry in _sceneIndex.Search(search))
                    if (entry.Target)
                        _rows.Add((entry.Id, entry.Name));
            }
            else if (_mode == "Bindings")
            {
                (_session.Definition?.Story?.RaidBindings ?? new())
                    .AsValueEnumerable()
                    .Where(b => b.Location.Length == 0 || b.Location == _session.Location)
                    .Select(b => (b.Id, b.Name + " Ã‚Â· " + b.Kind + " Ã‚Â· " + (b.ZoneId.Length > 0 ? b.ZoneId : b.ObjectPath)))
                    .CopyTo(_rows);
            }
            else if (_mode == "AI")
            {
                // AI records are presented by the pooled tree below. Keep the
                // legacy row list empty so generic pagination cannot flatten or
                // filter away its ancestors.
            }
            else
            {
                if (_mode == "Zones" && EditorMode.Ready)
                {
                    FilterZonesForLayout(_layoutId)
                        .AsValueEnumerable()
                        .Where(r => r.Location == _session.Location)
                        .Select(r => (r.Id, r.Name + " Ã‚Â· " + ZoneOwnerName(r)))
                        .CopyTo(_rows);
                }
                else
                {
                    IEnumerable<SpatialCapture>? records = _mode == "Zones" ? _session.Definition?.Zones : _session.Definition?.Captures;
                    if (records != null)
                        records.AsValueEnumerable().Where(r => r.Location == _session.Location).Select(r => (r.Id, r.Name)).CopyTo(_rows);
                }
            }

            if (_mode != "Scene" && !treeMode)
            {
                _rows.RemoveAll(r => r.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0);
            }

            if (_mode == "Scene")
                _rows.Sort(
                    (a, b) =>
                    {
                        var order = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label);
                        return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id);
                    }
                );
        }
        if (treeMode)
        {
            var contextId =
                _mode == "AI" ? "AI:" + _layoutId
                : _mode == "Routes" ? "Routes:" + _session.Location
                : "Zones:" + _layoutId;
            if (_mode == "AI")
            {
                view.RefreshTree(
                    contextId,
                    _session.ContentVersion,
                    search,
                    _selected,
                    expanded => RaidEditorAiTree.Build(Layout, search, expanded)
                );
            }
            else if (_mode == "Routes")
            {
                view.RefreshTree(
                    contextId,
                    _session.ContentVersion,
                    search,
                    _selected,
                    expanded =>
                        EditorTreeModel.Create(
                            contextId,
                            EditorLibraryTrees.Routes(_session.Definition?.MapLayouts ?? new(), _session.Location),
                            search,
                            expanded
                        )
                );
            }
            else
            {
                view.RefreshTree(
                    contextId,
                    _session.ContentVersion,
                    search,
                    _selected,
                    expanded =>
                        EditorTreeModel.Create(
                            contextId,
                            EditorLibraryTrees.Zones(FilterZonesForLayout(_layoutId), _session.Definition?.MapLayouts ?? new()),
                            search,
                            expanded
                        )
                );
            }
        }
        else
        {
            view.HideTree();
        }
        if (!RemoteCatalog)
            _page = Math.Min(_page, Math.Max(0, (_rows.Count - 1) / 10));
        for (var i = 0; i < 10 && !treeMode; i++)
        {
            var index = LibraryOffset + i;
            view.Get<EditorButton>("Row" + i).Identity = index < _rows.Count ? _rows[index].Id : "";
            view.Caption("Row" + i, index < _rows.Count ? _rows[index].Label : "");
            view.Get<Button>("Row" + i).interactable = index < _rows.Count;
            view.Visible("Row" + i, index < _rows.Count);
        }
        view.Caption("Snap", _snap ? "Snap: on" : "Snap: off");
        var point = Selected;
        view.Caption("AddBox", _mode == "Bindings" ? "+ Trigger" : "+ Box");
        view.Caption("AddSphere", _mode == "Bindings" ? "+ Interaction" : "+ Sphere");
        var zone = point as SeasonZone;
        RefreshZoneScope(view, zone);
        var ownerName = zone == null ? "Shared" : ZoneOwnerName(zone);
        if (!EditorMode.Ready || Layout == null)
            _zoneCreateShared = true;
        var creationScopes = new List<Dropdown.OptionData> { new("New: Shared") };
        if (EditorMode.Ready && Layout != null)
            creationScopes.Add(new Dropdown.OptionData("New: Layout"));
        view.SetDropdown("ZoneCreateScope", creationScopes, _zoneCreateShared ? 0 : creationScopes.Count - 1);
        view.Visible("EventKind", _mode == "Bindings" && Binding != null);
        view.Visible("Identity", _mode != "Bindings" || Binding == null);
        view.Get<Button>("Complete").interactable = _task != null && !_session.Busy && _session.Conflict == null;
        view.Caption("EventKind", "Event kind: " + (Binding?.Kind ?? "Trigger"));
        view.Value("Name", point?.Name ?? (_mode == "Bindings" ? Binding?.Name : "") ?? "");
        view.Text("Identity", point == null ? Binding?.Id ?? "Select a record" : point.Id + " Ã‚Â· " + point.Scene);
        foreach (var group in new[] { "Position", "Rotation", "Size" })
        {
            var vector =
                group == "Position" ? point?.Position
                : group == "Rotation" ? point?.Rotation
                : (point as SeasonZone)?.Size;
            var values = vector == null ? new[] { 0f, 0f, 0f } : new[] { vector.X, vector.Y, vector.Z };
            for (var i = 0; i < 3; i++)
            {
                view.Value(group + "XYZ"[i], values[i].ToString("0.###", CultureInfo.InvariantCulture));
            }
        }
        view.Value("Radius", ((point as SeasonZone)?.Radius ?? 0).ToString("0.###", CultureInfo.InvariantCulture));
        foreach (var use in new[] { "InZone", "VisitPlace", "LeaveItemAtLocation" })
        {
            view.Caption(
                use,
                ((point as SeasonZone)?.Uses.Contains(use) == true ? "Ã¢Å“â€œ " : "")
                    + (
                        use == "InZone" ? "In zone"
                        : use == "VisitPlace" ? "Visit"
                        : "Place item"
                    )
            );
        }

        var details = point is SeasonZone z
            ? "Ownership: "
                + ownerName
                + "\n"
                + z.Shape
                + " Ã‚Â· "
                + (Inside(z, _player!.Transform.position) ? "Player inside" : "Player outside")
                + "\nPreview only Ã‚Â· "
                + _tool
                + " handles\n"
                + string.Join(", ", SpatialRules.Uses(_session.Definition!, z.Id))
            : "Preview only Ã‚Â· no gameplay changes";
        if (_picked)
        {
            details =
                PickedPath
                + "\n"
                + _picked!.GetComponents<Component>().AsValueEnumerable().Where(c => c).Select(c => c.GetType().Name).JoinToString(", ")
                + "\n"
                + ObjectError();
        }

        view.Text("Details", details);
        view.Caption("UseObject", point is SeasonZone && Binding != null ? "Bind selected zone" : "Use scene target");
        RefreshMaps(geometry);
        RefreshWorkspace();
        RefreshAiWorkspace();
        PresentScene();
    }

    private string ZoneOwnerName(SeasonZone zone)
    {
        if (string.IsNullOrEmpty(zone.LayoutId))
            return "Shared";

        return _session?.Definition?.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == zone.LayoutId)?.Name ?? zone.LayoutId;
    }

    private string ZoneScopeName(string layoutId) =>
        string.IsNullOrEmpty(layoutId)
            ? "Shared"
            : _session?.Definition?.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == layoutId)?.Name ?? layoutId;

    private void RefreshZoneScope(RaidEditorView view, SeasonZone? zone)
    {
        _zoneScopeIds.Clear();
        var options = new List<Dropdown.OptionData> { new("Shared") };
        _zoneScopeIds.Add("");
        var layouts =
            _session?.Definition?.MapLayouts.AsValueEnumerable().Where(l => l.Location == _session.Location).ToArray()
            ?? Array.Empty<MapLayout>();
        foreach (var layout in layouts)
        {
            _zoneScopeIds.Add(layout.Id);
            options.Add(new Dropdown.OptionData(layout.Name.Length == 0 ? layout.Id : layout.Name));
        }

        if (zone != null && !string.IsNullOrEmpty(zone.LayoutId) && !_zoneScopeIds.Contains(zone.LayoutId))
        {
            _zoneScopeIds.Add(zone.LayoutId);
            options.Add(new Dropdown.OptionData("Missing layout Ã‚Â· " + zone.LayoutId));
        }

        var selected = zone == null ? 0 : _zoneScopeIds.IndexOf(zone.LayoutId ?? "");
        view.SetDropdown("ZoneScope", options, selected < 0 ? 0 : selected);
    }
}
