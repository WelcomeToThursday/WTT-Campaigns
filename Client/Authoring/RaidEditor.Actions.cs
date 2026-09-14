using System.Globalization;
using EFT.Ballistics;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Client.Story;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Shared.Story;
using ZLinq;

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

            foreach (var mode in new[] { "Maps", "Routes", "Zones", "Bindings", "Captures", "Scene" })
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
                        _page = previous.Page;
                        Refresh();
                        view.Windows.BrowseCategory();
                    }
                );
            }
            for (var i = 0; i < 10; i++)
            {
                var binding = view.Get<Button>("Row" + i).gameObject.AddComponent<WTT.Campaigns.UI.Controls.EditorRowSelection>();
                Button("Row" + i, () => SelectRow(binding.Consume()));
            }
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
            Button("Capture", () => Capture(false));
            Button(
                "Pick",
                () =>
                {
                    _picking = true;
                    _notice = "Click a scene object. Use Select parent to choose its binding target.";
                }
            );
            Button("Undo", () => { CancelDrag(); _session?.Undo(false); });
            Button("Redo", () => { CancelDrag(); _session?.Undo(true); });
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
                        if (SceneWorkspace) SceneTransform(value);
                        else _tool = value;
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
                                    })
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
        if (SceneWorkspace && !CanTransformScene("Move")) return;
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
                }
            )
        );
        _selected = id;
        _mode = "Zones";
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
        if (id.Length == 0) return;
        CancelDrag();
        if (MapWorkspace)
        {
            if (_session?.Definition?.MapLayouts.AsValueEnumerable().Any(l => l.Id == id) == true)
                _layoutId = id;
            _selected = id;
            if (EditorMode.Ready && MapPoint is MapObjectEdit or MapLootPlacement)
            {
                EnterSceneSelection();
                SelectSceneRow(id);
            }
        }
        else if (_mode == "Scene")
        {
            if (SceneWorkspace) SelectSceneRow(id);
            else _picked = _sceneIndex.Entries[int.Parse(id, CultureInfo.InvariantCulture)].Target;
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
        view.Text(
            "Connection",
            (_session.Definition?.Name ?? "Waiting for a connected draft")
                + " / "
                + _session.Location
                + (EditorMode.Ready ? " / " + (Layout?.Name ?? "New layout") : "")
        );
        view.Text(
            "Request",
            _task == null ? "RAID CONTINUES · Player remains in place" : "RAID CONTINUES · " + _task.Tool + " capture requested"
        );
        view.Text("Status", _session.Status + (_notice.Length > 0 ? "\n" + _notice : ""));
        view.Conflict(_session);
        // Do not repurpose or hide a row between pointer-down and pointer-up.
        if (view.Root.GetComponentsInChildren<WTT.Campaigns.UI.Controls.EditorRowSelection>()
            .AsValueEnumerable().Any(row => row.Pressed))
        {
            _passiveState = "";
            return;
        }
        foreach (var mode in new[] { "Maps", "Routes", "Zones", "Bindings", "Captures", "Scene" })
            view.Highlight(mode, _mode == mode);
        var search = view.Get<InputField>("Search").text;
        var libraryKey = $"{_mode}|{_sceneTab}|{_sceneFilter}|{search}|{_layoutId}|{_session.ContentVersion}|{_sceneIndex.Count}|{_catalogGeneration}|{_catalogLoading}|{(RemoteCatalog ? _page : 0)}";
        if (_libraryKey != libraryKey)
        {
            _libraryKey = libraryKey;
            _rows.Clear();
            if (MapWorkspace && _session.Definition != null)
                MapRows();
            else if (SceneWorkspace) SceneRows(search);
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
                    .Select(b => (b.Id, b.Name + " · " + b.Kind + " · " + (b.ZoneId.Length > 0 ? b.ZoneId : b.ObjectPath)))
                    .CopyTo(_rows);
            }
            else
            {
                IEnumerable<SpatialCapture>? records = _mode == "Zones" ? _session.Definition?.Zones : _session.Definition?.Captures;
                if (records != null)
                {
                    records.AsValueEnumerable().Where(r => r.Location == _session.Location).Select(r => (r.Id, r.Name)).CopyTo(_rows);
                }
            }

            if (_mode != "Scene")
            {
                _rows.RemoveAll(r => r.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0);
            }

            if (_mode == "Scene") _rows.Sort((a, b) =>
            {
                var order = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label);
                return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });
        }
        if (!RemoteCatalog) _page = Math.Min(_page, Math.Max(0, (_rows.Count - 1) / 10));
        for (var i = 0; i < 10; i++)
        {
            var index = LibraryOffset + i;
            view.Get<WTT.Campaigns.UI.Controls.EditorRowSelection>("Row" + i).Identity = index < _rows.Count ? _rows[index].Id : "";
            view.Caption("Row" + i, index < _rows.Count ? _rows[index].Label : "");
            view.Get<Button>("Row" + i).interactable = index < _rows.Count;
            view.Visible("Row" + i, index < _rows.Count);
        }
        view.Caption("Snap", _snap ? "Snap: on" : "Snap: off");
        var point = Selected;
        view.Caption("AddBox", _mode == "Bindings" ? "+ Trigger" : "+ Box");
        view.Caption("AddSphere", _mode == "Bindings" ? "+ Interaction" : "+ Sphere");
        view.Get<Button>("EventKind").gameObject.SetActive(_mode == "Bindings" && Binding != null);
        view.Get<UnityEngine.UI.Text>("Identity").gameObject.SetActive(_mode != "Bindings" || Binding == null);
        view.Get<Button>("Complete").interactable = _task != null && !_session.Busy && _session.Conflict == null;
        view.Caption("EventKind", "Event kind: " + (Binding?.Kind ?? "Trigger"));
        view.Value("Name", point?.Name ?? (_mode == "Bindings" ? Binding?.Name : "") ?? "");
        view.Text("Identity", point == null ? Binding?.Id ?? "Select a record" : point.Id + " · " + point.Scene);
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
                ((point as SeasonZone)?.Uses.Contains(use) == true ? "✓ " : "")
                    + (
                        use == "InZone" ? "In zone"
                        : use == "VisitPlace" ? "Visit"
                        : "Place item"
                    )
            );
        }

        var details = point is SeasonZone z
            ? z.Shape
                + " · "
                + (Inside(z, _player!.Transform.position) ? "Player inside" : "Player outside")
                + "\nPreview only · "
                + _tool
                + " handles\n"
                + string.Join(", ", SpatialRules.Uses(_session.Definition!, z.Id))
            : "Preview only · no gameplay changes";
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
        PresentScene();
    }
}
