using System.Globalization;
using EFT.Ballistics;
using SeasonalPerks.Client.Spatial;
using SeasonalPerks.Client.Story;
using SeasonalPerks.Shared.Spatial;
using SeasonalPerks.Shared.Story;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.Client.Authoring;

public sealed partial class RaidEditor
{
    private StoryRaidBinding? Binding
    {
        get
        {
            return _session?.Definition?.Story?.RaidBindings.FirstOrDefault(b =>
                b.Id == (_task?.TargetKind == "Binding" ? _task.TargetId : _bindingTarget)
            );
        }
    }

    private string _bindingTarget = "";

    private RaidEditorView BuildView()
    {
        var view = new RaidEditorView();
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

        foreach (var mode in new[] { "Zones", "Bindings", "Captures", "Scene" })
        {
            var value = mode;
            Button(
                mode,
                () =>
                {
                    _mode = value;
                    _page = 0;
                    Refresh();
                }
            );
        }
        for (var i = 0; i < 10; i++)
        {
            var row = i;
            Button("Row" + i, () => SelectRow(row));
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
                if ((_page + 1) * 10 < _rows.Count)
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
        Button("Undo", () => _session?.Undo(false));
        Button("Redo", () => _session?.Undo(true));
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
                    _session?.Edit(s => s.Story!.RaidBindings.Single(b => b.Id == id).Name = value.Trim());
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
                    var b = s.Story!.RaidBindings.Single(x => x.Id == id);
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
        return view;
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
        var id = _selected;
        _session?.Edit(s =>
        {
            var point = s.Zones.Cast<SpatialCapture>().Concat(s.Captures).FirstOrDefault(p => p.Id == id);
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
            ObjectPath = sceneObject ? StoryRaidRuntime.ObjectPath(_picked!) : "",
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
        var uses = SpatialRules.Uses(_session.Definition, _selected).ToArray();
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

    private void IndexScene()
    {
        _scene = Resources
            .FindObjectsOfTypeAll<Transform>()
            .Where(t =>
                t
                && t.gameObject.scene.IsValid()
                && !t.name.StartsWith("Seasonal", StringComparison.Ordinal)
                && !t.GetComponentInParent<Canvas>()
                && !t.GetComponentInParent<EFT.Player>()
            )
            .OrderBy(t => t.name)
            .ToList();
    }

    private void SelectRow(int row)
    {
        var index = _page * 10 + row;
        if (index >= _rows.Count)
        {
            return;
        }

        var id = _rows[index].Id;
        if (_mode == "Scene")
        {
            _picked = _scene[int.Parse(id, CultureInfo.InvariantCulture)];
        }
        else if (_mode == "Bindings")
        {
            _bindingTarget = id;
            _selected = "";
            var binding = Binding;
            _picked = _scene.FirstOrDefault(t => t && StoryRaidRuntime.ObjectPath(t) == binding?.ObjectPath);
        }
        else
        {
            _selected = id;
            _picked = null;
            if (Selected?.ObjectPath is { Length: > 0 } path)
            {
                _picked = _scene.FirstOrDefault(t => t && StoryRaidRuntime.ObjectPath(t) == path);
            }
        }
        Refresh();
    }

    private string ObjectError()
    {
        if (!_picked)
        {
            return "Pick a scene target first.";
        }

        var path = StoryRaidRuntime.ObjectPath(_picked!);
        if (_scene.Count(t => t && StoryRaidRuntime.ObjectPath(t) == path) != 1)
        {
            return "This path is ambiguous. Select a uniquely named target.";
        }

        if (Binding?.Kind == "Shoot" && !_picked!.GetComponent<BallisticCollider>())
        {
            return "Shoot targets need a ballistic collider on the selected object.";
        }

        if (Binding?.Kind is "Trigger" or "Cinematic" && !_picked!.GetComponents<Collider>().Any(c => c.isTrigger))
        {
            return "This event needs an existing trigger collider or an authored zone.";
        }

        if (Binding?.Kind == "Interact" && !_picked!.GetComponentsInChildren<Collider>().Any())
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
                var binding = s.Story!.RaidBindings.Single(b => b.Id == id);
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
        var path = StoryRaidRuntime.ObjectPath(_picked!);
        _session.Edit(s =>
        {
            var binding = s.Story!.RaidBindings.Single(b => b.Id == bindingId);
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

    private void Refresh(bool geometry)
    {
        if (_view == null || !_open || _session == null)
        {
            return;
        }

        var view = _view;
        view.Text("Connection", _session.Definition?.Name ?? "Waiting for a connected draft");
        view.Text(
            "Request",
            _task == null ? "RAID CONTINUES · Player remains in place" : "RAID CONTINUES · " + _task.Tool + " capture requested"
        );
        view.Text("Status", _session.Status + (_notice.Length > 0 ? "\n" + _notice : ""));
        view.Conflict(_session);
        var search = view.Get<InputField>("Search").text;
        _rows.Clear();
        if (_mode == "Scene")
        {
            _rows.AddRange(
                _scene
                    .Select((t, i) => (t, i))
                    .Where(x =>
                        x.t
                        && (search.Length == 0 || StoryRaidRuntime.ObjectPath(x.t).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    )
                    .Select(x => (x.i.ToString(CultureInfo.InvariantCulture), x.t.name))
            );
        }
        else if (_mode == "Bindings")
        {
            _rows.AddRange(
                (_session.Definition?.Story?.RaidBindings ?? new())
                    .Where(b => b.Location.Length == 0 || b.Location == _session.Location)
                    .Select(b => (b.Id, b.Name + " · " + b.Kind + " · " + (b.ZoneId.Length > 0 ? b.ZoneId : b.ObjectPath)))
            );
        }
        else
        {
            _rows.AddRange(
                (_mode == "Zones" ? _session.Definition?.Zones.Cast<SpatialCapture>() : _session.Definition?.Captures) is { } records
                    ? records.Where(r => r.Location == _session.Location).Select(r => (r.Id, r.Name))
                    : Enumerable.Empty<(string, string)>()
            );
        }

        if (_mode != "Scene")
        {
            _rows.RemoveAll(r => r.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0);
        }

        _page = Math.Min(_page, Math.Max(0, (_rows.Count - 1) / 10));
        for (var i = 0; i < 10; i++)
        {
            var index = _page * 10 + i;
            view.Caption("Row" + i, index < _rows.Count ? _rows[index].Label : "");
            view.Get<Button>("Row" + i).interactable = index < _rows.Count;
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
                StoryRaidRuntime.ObjectPath(_picked!)
                + "\n"
                + string.Join(", ", _picked!.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name))
                + "\n"
                + ObjectError();
        }

        view.Text("Details", details);
        view.Caption("UseObject", point is SeasonZone && Binding != null ? "Bind selected zone" : "Use scene target");
        if (geometry)
        {
            DrawGeometry();
        }
    }
}
