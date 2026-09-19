using System.Globalization;
using UnityEngine;
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
    private string _aiSelectionKind = "";

    private readonly struct AiSelection
    {
        internal AiSelection(
            string kind,
            MapEncounter? encounter = null,
            MapEncounterWave? wave = null,
            MapEncounterRosterEntry? roster = null,
            SpatialCapture? spawn = null,
            MapPatrolRoute? route = null,
            SpatialCapture? waypoint = null
        )
        {
            Kind = kind;
            Encounter = encounter;
            Wave = wave;
            Roster = roster;
            Spawn = spawn;
            Route = route;
            Waypoint = waypoint;
        }

        internal string Kind { get; }
        internal MapEncounter? Encounter { get; }
        internal MapEncounterWave? Wave { get; }
        internal MapEncounterRosterEntry? Roster { get; }
        internal SpatialCapture? Spawn { get; }
        internal MapPatrolRoute? Route { get; }
        internal SpatialCapture? Waypoint { get; }
        internal MapVolume? TriggerVolume => Encounter?.Trigger?.Volume;
        internal SpatialCapture? Point =>
            Kind switch
            {
                "spawn" => Spawn,
                "waypoint" => Waypoint,
                "trigger" => TriggerVolume,
                _ => null,
            };
        internal string Id =>
            Point?.Id
            ?? (
                Kind == "enc" ? Encounter?.Id
                : Kind == "wave" ? Wave?.Id
                : Kind == "roster" ? Roster?.Id
                : Kind == "route" ? Route?.Id
                : ""
            )
            ?? "";
        internal string Name =>
            Point?.Name
            ?? (
                Kind == "enc" ? Encounter?.Name
                : Kind == "wave" ? Wave?.Name
                : Kind == "roster" ? Roster?.Id
                : Kind == "route" ? Route?.Name
                : ""
            )
            ?? "";

        // The default struct is used when the active layout has no matching
        // selection. Its auto-property backing field is null until a value is
        // assigned, so validity must tolerate the default value.
        internal bool Valid => !string.IsNullOrEmpty(Kind);
    }

    private bool AiWorkspace => _mode == "AI";
    private bool _inspectAiNavigation;
    private readonly List<string> _aiSpawnChoices = new();
    private readonly List<string> _aiPatrolChoices = new();

    private void BindAiControls(RaidEditorView view)
    {
        _ = new MissionEditorPanel(view, view.ElementForTool("AI", "AiTools"), () => _session);
        RaidEditorAiContracts.LayoutProvider = () => Layout;
        view.Button("AiEncounter", AddAiEncounter);
        view.Button("AiWave", AddAiWave);
        view.Button("AiRoster", AddAiRoster);
        view.Button("AiSpawn", AddAiSpawn);
        view.Button("AiPatrol", AddAiPatrol);
        view.Button("AiWaypoint", AddAiWaypoint);
        view.Button("AiTrigger", CycleAiTrigger);
        view.Button("AiObserve", () => BeginAiPreview(false));
        view.Dropdown(
            "AiPlaytestGear",
            i =>
            {
                if (!AiPreviewBusy)
                    _aiUseProfileKit = i == 1;
                Refresh();
            }
        );
        view.Button("AiPlaytest", () => BeginAiPreview(true));
        view.Button("TestCheckpoints", TestEditorCheckpoints);
        view.Button("AiReset", EndAiPreview);
        view.Button("AiSimulate", SimulateSelectedAiEvent);
        view.Button(
            "AiNavigation",
            () =>
            {
                _inspectAiNavigation = !_inspectAiNavigation;
                RefreshAiWorkspace();
            }
        );
        view.Button("AiWaveWaitPrevious", ToggleAiWaveWait);
        view.Dropdown("AiRosterRole", SetAiRole);
        view.Dropdown("AiRosterDifficulty", SetAiDifficulty);
        view.Dropdown(
            "AiRosterSpawnNext",
            index =>
            {
                var selected = AiSelected(out var kind);
                if (kind != "roster" || selected.Roster == null || index <= 0 || index > _aiSpawnChoices.Count)
                    return;
                var ids = new List<string>(selected.Roster.SpawnPointIds);
                var id = _aiSpawnChoices[index - 1];
                if (!ids.Remove(id))
                    ids.Add(id);
                EditAiSpawnPoints(string.Join(",", ids));
            }
        );
        view.Dropdown(
            "AiRosterPatrolNext",
            index =>
            {
                if (index >= 0 && index < _aiPatrolChoices.Count)
                    EditAiRosterText("PatrolRouteId", _aiPatrolChoices[index]);
            }
        );
        view.Dropdown("AiPace", SetAiPace);
        view.Dropdown("AiCompletion", SetAiCompletion);
        view.Input("AiTriggerEventId", value => EditAiTriggerText("EventId", value));
        view.Input("AiTriggerZoneId", value => EditAiTriggerText("ZoneId", value));
        view.Input("AiWaveDelaySeconds", value => EditAiWaveDelay(value));
        view.Input("AiRosterCount", value => EditAiRosterCount(value));
        view.Input("AiRosterSquadId", value => EditAiRosterText("SquadId", value));
        view.Input("AiRosterSpawnPoints", EditAiSpawnPoints);
        view.Input("AiRosterPatrolRoute", value => EditAiRosterText("PatrolRouteId", value));
        view.Input("AiWaypointWaitSeconds", EditAiWaypointWait);
    }

    private void EditAi(Action<MapLayout> edit)
    {
        if (_session?.Definition == null || AiPreviewBusy || _session.Previewing || !AiWorkspace || _layoutId.Length == 0)
            return;
        _session.Edit(definition =>
        {
            var layout = definition.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == _layoutId);
            if (layout == null)
                throw new InvalidOperationException("Select a mission layout before authoring AI encounters.");
            edit(layout);
        });
        _libraryKey = "";
        Refresh();
    }

    private static string Display(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static IEnumerable<T> Items<T>(IEnumerable<T>? values) => values ?? Array.Empty<T>();

    private static string AiRoleDisplay(string? role) =>
        role switch
        {
            "assault" => "Scav",
            "pmcUSEC" => "USEC",
            "pmcBEAR" => "BEAR",
            null or "" => "Unsupported",
            _ => "Unsupported · " + role,
        };

    private AiSelection AiSelected(out string kind)
    {
        kind = "";
        if (Layout == null || !TryAiSelection(Layout, _selected, out var selection))
            return default;
        kind = selection.Kind;
        return selection;
    }

    private static bool TryAiSelection(MapLayout layout, string selected, out AiSelection selection)
    {
        selection = default;
        var parts = selected.Split(':');
        if (parts.Length < 2)
            return false;

        if (parts[0] == "enc")
        {
            var encounter = (Items(layout.Encounters)).AsValueEnumerable().FirstOrDefault(e => e?.Id == parts[1]);
            if (encounter == null)
                return false;
            selection = new AiSelection("enc", encounter: encounter);
            return true;
        }

        if (parts[0] == "trigger")
        {
            var encounter = (Items(layout.Encounters)).AsValueEnumerable().FirstOrDefault(e => e?.Id == parts[1]);
            if (encounter?.Trigger?.Volume == null)
                return false;
            selection = new AiSelection("trigger", encounter: encounter);
            return true;
        }

        if (parts[0] == "spawn")
        {
            var spawn = (Items(layout.SpawnPoints)).AsValueEnumerable().FirstOrDefault(p => p?.Id == parts[1]);
            if (spawn == null)
                return false;
            selection = new AiSelection("spawn", spawn: spawn);
            return true;
        }

        if (parts[0] == "route")
        {
            var route = (Items(layout.PatrolRoutes)).AsValueEnumerable().FirstOrDefault(r => r?.Id == parts[1]);
            if (route == null)
                return false;
            selection = new AiSelection("route", route: route);
            return true;
        }

        if (parts[0] == "waypoint" && parts.Length >= 3)
        {
            var route = (Items(layout.PatrolRoutes)).AsValueEnumerable().FirstOrDefault(r => r?.Id == parts[1]);
            var waypoint = Items(route?.Waypoints).AsValueEnumerable().FirstOrDefault(p => p?.Id == parts[2]);
            if (route == null || waypoint == null)
                return false;
            selection = new AiSelection("waypoint", route: route, waypoint: waypoint);
            return true;
        }

        var encounterForChild = (Items(layout.Encounters)).AsValueEnumerable().FirstOrDefault(e => e?.Id == parts[1]);
        var wave = Items(encounterForChild?.Waves).AsValueEnumerable().FirstOrDefault(w => w?.Id == (parts.Length >= 3 ? parts[2] : ""));
        if (wave == null)
            return false;
        if (parts[0] == "wave")
        {
            selection = new AiSelection("wave", encounter: encounterForChild, wave: wave);
            return true;
        }

        if (parts[0] == "roster" && parts.Length >= 4)
        {
            var roster = Items(wave.Roster).AsValueEnumerable().FirstOrDefault(r => r?.Id == parts[3]);
            if (roster == null)
                return false;
            selection = new AiSelection("roster", encounter: encounterForChild, wave: wave, roster: roster);
            return true;
        }
        return false;
    }

    private SpatialCapture? AiSelectedPoint()
    {
        var selected = AiSelected(out _);
        var point = selected.Point;
        if (point == null)
            return null;
        if (point is MapVolume volume)
            return RaidEditorSession.Copy(volume);
        return RaidEditorSession.Copy(point);
    }

    private List<string> ValidateAiLayout()
    {
        if (_session?.Definition == null)
            return new List<string>();
        var layout = _session.Definition.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == _layoutId);
        if (layout == null)
            return new List<string> { "Select a mission layout before previewing encounters." };
        return MapEncounterRules.Errors(layout, new RaidEditorAiContracts.Navigation(), true, true);
    }

    private bool TryValidateAiRoute(MapPatrolRoute route, Action<MapPatrolRoute> edit)
    {
        var candidate = RaidEditorSession.Copy(route);
        edit(candidate);
        var error = RaidEditorAiContracts.RouteError(candidate);
        if (error.Length == 0)
            return true;
        _notice = "Edit rejected: " + error;
        return false;
    }

    private bool TryValidateSelectedAiWaypoint(string selectedId, Action<SpatialCapture> edit)
    {
        if (
            Layout == null
            || !TryAiSelection(Layout, selectedId, out var selected)
            || selected.Kind != "waypoint"
            || selected.Route == null
            || selected.Waypoint == null
        )
            return true;

        return TryValidateAiRoute(
            selected.Route,
            route =>
            {
                var waypoint = Items(route.Waypoints).AsValueEnumerable().FirstOrDefault(point => point?.Id == selected.Waypoint.Id);
                if (waypoint != null)
                    edit(waypoint);
            }
        );
    }

    private void AddAiEncounter()
    {
        if (Layout == null)
        {
            _notice = "Select a layout before adding an encounter.";
            return;
        }
        var encounterId = RaidEditorAiContracts.Id();
        var waveId = RaidEditorAiContracts.Id();
        var rosterId = RaidEditorAiContracts.Id();
        EditAi(layout =>
            layout.Encounters.Add(
                new MapEncounter
                {
                    Id = encounterId,
                    Name = "New encounter",
                    Trigger = new MapEncounterTrigger { Type = MapEncounterTrigger.MissionStart },
                    Waves = new()
                    {
                        new MapEncounterWave
                        {
                            Id = waveId,
                            Name = "Wave 1",
                            DelaySeconds = 0,
                            WaitForPreviousWave = false,
                            Roster = new()
                            {
                                new MapEncounterRosterEntry
                                {
                                    Id = rosterId,
                                    Role = "assault",
                                    Difficulty = "normal",
                                    Count = 1,
                                },
                            },
                        },
                    },
                }
            )
        );
        _selected = "enc:" + encounterId;
        Refresh();
    }

    private void AddAiWave()
    {
        var selected = AiSelected(out var kind);
        if (!selected.Valid || selected.Encounter == null || kind != "enc")
        {
            _notice = "Select an encounter before adding a wave.";
            return;
        }
        var encounterId = selected.Encounter.Id;
        var id = RaidEditorAiContracts.Id();
        EditAi(layout =>
        {
            var encounter = layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == encounterId);
            encounter?.Waves.Add(new MapEncounterWave { Id = id, Name = "Wave" });
        });
        _selected = "wave:" + encounterId + ":" + id;
        Refresh();
    }

    private void AddAiRoster()
    {
        var selected = AiSelected(out var kind);
        var encounter = selected.Encounter;
        var wave = kind == "enc" ? Items(encounter?.Waves).AsValueEnumerable().FirstOrDefault() : selected.Wave;
        if (!selected.Valid || encounter == null || wave == null || kind is not ("enc" or "wave" or "roster"))
        {
            _notice = "Select an encounter or wave before adding a roster entry.";
            return;
        }
        var encounterId = encounter.Id;
        var waveId = wave.Id;
        var id = RaidEditorAiContracts.Id();
        EditAi(layout =>
        {
            var target = Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == encounterId)?.Waves)
                .AsValueEnumerable()
                .FirstOrDefault(w => w.Id == waveId);
            if (target == null)
                throw new InvalidOperationException("The selected encounter wave no longer exists.");
            var firstSpawn = layout.SpawnPoints.AsValueEnumerable().FirstOrDefault()?.Id ?? "";
            target.Roster.Add(
                new MapEncounterRosterEntry
                {
                    Id = id,
                    Role = "assault",
                    Difficulty = "normal",
                    Count = 1,
                    SpawnPointIds = firstSpawn.Length == 0 ? new() : new() { firstSpawn },
                }
            );
        });
        _selected = "roster:" + encounterId + ":" + waveId + ":" + id;
        Refresh();
    }

    private bool TryAiPlacement(out Vector3 position, out string scene)
    {
        position = default;
        scene = "";
        if (!_open || !_camera || !TryRouteFloor(_flyPosition, 20, out var floor))
        {
            _notice = "Move the editor camera above a floor within 20 metres to place an AI marker.";
            return false;
        }
        position = floor.point;
        scene = floor.transform.gameObject.scene.name;
        if (!RaidEditorAiContracts.TryNav(position, out var safe) || (safe - position).sqrMagnitude > .2f * .2f)
        {
            _notice = "Placement rejected: choose a clear NavMesh standing area.";
            return false;
        }
        position = safe;
        return true;
    }

    private void AddAiSpawn()
    {
        if (Layout == null || !TryAiPlacement(out var position, out var scene))
            return;
        var id = RaidEditorAiContracts.Id();
        EditAi(layout =>
            layout.SpawnPoints.Add(
                new SpatialCapture
                {
                    Id = id,
                    Name = "New spawn point",
                    Location = _session!.Location,
                    Scene = scene,
                    Position = ZoneRuntime.Vector(position),
                    Rotation = new SpatialVector { Y = _flyRotation.eulerAngles.y },
                }
            )
        );
        _selected = "spawn:" + id;
        Refresh();
    }

    private void AddAiPatrol()
    {
        if (Layout == null)
        {
            _notice = "Select a layout before adding a patrol route.";
            return;
        }
        var id = RaidEditorAiContracts.Id();
        EditAi(layout =>
            layout.PatrolRoutes.Add(
                new MapPatrolRoute
                {
                    Id = id,
                    Name = "New patrol",
                    Completion = MapPatrolRoute.Loop,
                    Pace = MapPatrolRoute.Walk,
                }
            )
        );
        _selected = "route:" + id;
        Refresh();
    }

    private void AddAiWaypoint()
    {
        var selected = AiSelected(out var kind);
        var route =
            kind == "route" ? selected.Route
            : kind == "waypoint" ? selected.Route
            : null;
        if (route == null || !TryAiPlacement(out var position, out var scene))
        {
            _notice = "Select a patrol route, then place its waypoint on the NavMesh.";
            return;
        }
        var routeId = route.Id;
        var id = RaidEditorAiContracts.Id();
        var waypoint = new SpatialCapture
        {
            Id = id,
            Name = "Waypoint " + ((route.Waypoints?.Count ?? 0) + 1),
            Location = _session!.Location,
            Scene = scene,
            Position = ZoneRuntime.Vector(position),
            Rotation = new SpatialVector { Y = _flyRotation.eulerAngles.y },
        };
        if (
            !TryValidateAiRoute(
                route,
                candidate =>
                {
                    candidate.Waypoints ??= new();
                    MapPatrolRouteEditing.Append(candidate, RaidEditorSession.Copy(waypoint));
                }
            )
        )
            return;
        EditAi(layout =>
        {
            var target = layout.PatrolRoutes.AsValueEnumerable().FirstOrDefault(r => r.Id == routeId);
            if (target == null)
                throw new InvalidOperationException("The selected patrol route no longer exists.");
            MapPatrolRouteEditing.Append(target, RaidEditorSession.Copy(waypoint));
        });
        _selected = "waypoint:" + routeId + ":" + id;
        Refresh();
    }

    private void EditAiName(string value)
    {
        var selectedId = _selected;
        EditAi(layout =>
        {
            if (!TryAiSelection(layout, selectedId, out var selected))
                return;
            var name = value.Trim();
            switch (selected.Kind)
            {
                case "enc":
                    if (selected.Encounter != null)
                        selected.Encounter.Name = name;
                    break;
                case "wave":
                    if (selected.Wave != null)
                        selected.Wave.Name = name;
                    break;
                case "roster":
                    break;
                case "spawn":
                case "waypoint":
                case "trigger":
                    if (selected.Point != null)
                        selected.Point.Name = name;
                    break;
                case "route":
                    if (selected.Route != null)
                        selected.Route.Name = name;
                    break;
            }
        });
    }

    private void EditAiPoint(SpatialCapture point)
    {
        var selectedId = _selected;
        if (
            point.Position == null
            || !RaidEditorAiContracts.TryNav(ZoneRuntime.Vector(point.Position), out var safe)
            || (safe - ZoneRuntime.Vector(point.Position)).sqrMagnitude > .001f
        )
        {
            _notice = "Position rejected: it is not on a clear NavMesh standing area.";
            return;
        }
        if (
            !TryValidateSelectedAiWaypoint(
                selectedId,
                waypoint =>
                {
                    waypoint.Name = point.Name;
                    waypoint.Scene = point.Scene;
                    waypoint.Position = RaidEditorSession.Copy(point.Position);
                    waypoint.Rotation = RaidEditorSession.Copy(point.Rotation);
                }
            )
        )
            return;
        EditAi(layout =>
        {
            if (!TryAiSelection(layout, selectedId, out var selected) || selected.Point == null)
                return;
            var target = selected.Point;
            target.Name = point.Name;
            target.Scene = point.Scene;
            target.Position = RaidEditorSession.Copy(point.Position);
            target.Rotation = RaidEditorSession.Copy(point.Rotation);
            if (target is MapVolume targetVolume && point is MapVolume sourceVolume)
            {
                targetVolume.Size = RaidEditorSession.Copy(sourceVolume.Size);
                targetVolume.Radius = sourceVolume.Radius;
            }
        });
    }

    private void DuplicateAiSelection()
    {
        var selected = AiSelected(out var kind);
        var sourceId = selected.Id;
        if (!selected.Valid || kind is not ("spawn" or "route" or "enc"))
        {
            _notice = "Select an encounter, spawn point or patrol route to duplicate it.";
            return;
        }
        var id = RaidEditorAiContracts.Id();
        EditAi(layout =>
        {
            if (kind == "spawn" && selected.Spawn != null)
            {
                var copy = RaidEditorSession.Copy(selected.Spawn);
                copy.Id = id;
                copy.Name = Display(copy.Name, "Spawn point") + " copy";
                layout.SpawnPoints.Add(copy);
            }
            else if (kind == "route" && selected.Route != null)
            {
                var copy = RaidEditorSession.Copy(selected.Route);
                copy.Id = id;
                copy.Name = Display(copy.Name, "Patrol route") + " copy";
                foreach (var waypoint in copy.Waypoints ?? new())
                    waypoint.Id = RaidEditorAiContracts.Id();
                layout.PatrolRoutes.Add(copy);
            }
            else if (kind == "enc" && selected.Encounter != null)
            {
                var copy = RaidEditorSession.Copy(selected.Encounter);
                copy.Id = id;
                copy.Name = Display(copy.Name, "Encounter") + " copy";
                if (copy.Trigger?.Volume != null)
                    copy.Trigger.Volume.Id = RaidEditorAiContracts.Id();
                foreach (var wave in copy.Waves ?? new())
                {
                    wave.Id = RaidEditorAiContracts.Id();
                    foreach (var roster in wave.Roster ?? new())
                        roster.Id = RaidEditorAiContracts.Id();
                }
                layout.Encounters.Add(copy);
            }
        });
        _selected = kind + ":" + id;
        Refresh();
    }

    private void EditAiVector(string group, int axis, float value)
    {
        var selectedId = _selected;
        if (
            group == "Position"
            && Layout != null
            && TryAiSelection(Layout, selectedId, out var selected)
            && selected.Point != null
            && selected.Point.Position != null
        )
        {
            var proposed = RaidEditorSession.Copy(selected.Point.Position);
            SetAxis(proposed, axis, value);
            var proposedWorld = ZoneRuntime.Vector(proposed);
            if (!RaidEditorAiContracts.TryNav(proposedWorld, out var safe) || (safe - proposedWorld).sqrMagnitude > .001f)
            {
                _notice = "Position rejected: it is not on a clear NavMesh standing area.";
                return;
            }
            if (
                !TryValidateSelectedAiWaypoint(
                    selectedId,
                    waypoint =>
                    {
                        if (waypoint.Position != null)
                            SetAxis(waypoint.Position, axis, value);
                    }
                )
            )
                return;
        }
        EditAi(layout =>
        {
            if (!TryAiSelection(layout, selectedId, out var selected) || selected.Point == null)
                return;
            var target = selected.Point;
            if (group == "Size" && target is MapVolume volume)
            {
                var size = RaidEditorSession.Copy(volume.Size);
                SetAxis(size, axis, value);
                if (!MapLayoutRules.Positive(size))
                    throw new InvalidOperationException("Volume dimensions must be positive.");
                volume.Size = size;
                return;
            }
            if (group is not ("Position" or "Rotation"))
                return;
            var vector = group == "Position" ? RaidEditorSession.Copy(target.Position) : RaidEditorSession.Copy(target.Rotation);
            SetAxis(vector, axis, value);
            if (group == "Position")
            {
                var proposed = ZoneRuntime.Vector(vector);
                if (!RaidEditorAiContracts.TryNav(proposed, out var safe) || (safe - proposed).sqrMagnitude > .001f)
                    throw new InvalidOperationException("Position rejected: it is not on a clear NavMesh standing area.");
            }
            if (group == "Position")
                target.Position = vector;
            else
                target.Rotation = vector;
        });
    }

    private static void SetAxis(SpatialVector vector, int axis, float value)
    {
        if (axis == 0)
            vector.X = value;
        else if (axis == 1)
            vector.Y = value;
        else
            vector.Z = value;
    }

    private void EditAiRadius(float value)
    {
        if (value <= 0 || !float.IsFinite(value))
            return;
        var selectedId = _selected;
        EditAi(layout =>
        {
            if (TryAiSelection(layout, selectedId, out var selected) && selected.Point is MapVolume volume)
                volume.Radius = value;
        });
    }

    private void DeleteAiSelection()
    {
        var selected = AiSelected(out var kind);
        var parts = _selected.Split(':');
        if (!selected.Valid || parts.Length < 2)
            return;
        var id = parts[1];
        EditAi(layout =>
        {
            if (kind == "enc")
                layout.Encounters.RemoveAll(e => e.Id == id);
            else if (kind == "spawn")
                layout.SpawnPoints.RemoveAll(p => p.Id == id);
            else if (kind == "route")
                layout.PatrolRoutes.RemoveAll(r => r.Id == id);
            else if (kind == "trigger")
            {
                var encounter = layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == id);
                if (encounter?.Trigger != null)
                    encounter.Trigger.Volume = null;
            }
            else if (kind == "waypoint" && parts.Length >= 3)
            {
                var route = layout.PatrolRoutes.AsValueEnumerable().FirstOrDefault(r => r.Id == id);
                if (route != null)
                {
                    var index = route.Waypoints.FindIndex(p => p.Id == parts[2]);
                    MapPatrolRouteEditing.RemoveAt(route, index);
                }
            }
            else if ((kind == "wave" || kind == "roster") && parts.Length >= 3)
            {
                var encounter = layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == id);
                var wave = Items(encounter?.Waves).AsValueEnumerable().FirstOrDefault(w => w.Id == parts[2]);
                if (kind == "wave")
                    encounter?.Waves.Remove(wave!);
                else if (wave != null && parts.Length >= 4)
                    wave.Roster.RemoveAll(r => r.Id == parts[3]);
            }
        });
        _selected = "";
        Refresh();
    }

    private bool AiAcceptPreview(SpatialCapture point, SpatialCapture before)
    {
        if (!AiWorkspace || point.Position == null)
            return true;
        var proposed = ZoneRuntime.Vector(point.Position);
        if (!RaidEditorAiContracts.TryNav(proposed, out var safe) || (safe - proposed).sqrMagnitude > .001f)
        {
            RestorePoint(point, before);
            _notice = "Drag rejected: the AI position must remain on a clear NavMesh standing area.";
            return false;
        }
        if (Layout != null && TryAiSelection(Layout, _selected, out var selected) && selected.Kind == "waypoint" && selected.Route != null)
        {
            var error = RaidEditorAiContracts.RouteError(selected.Route);
            if (error.Length > 0)
            {
                RestorePoint(point, before);
                _notice = "Drag rejected: " + error;
                return false;
            }
        }
        return true;
    }

    private void RefreshAiWorkspace()
    {
        if (_view?.Valid != true)
            return;
        var visible = AiWorkspace;
        _view.Visible("AiTools", visible);
        _view.Visible("AiToolsScroll", visible);
        _view.InspectNavigation(visible && _inspectAiNavigation);
        _view.Caption("AiNavigation", "Inspect navigation: " + (_inspectAiNavigation ? "on" : "off"));
        _view.Windows.SetTooltip(
            "AiNavigation",
            "Select a spawn to see nearby navigation samples, authored cuts, and its core connection. Local cores are created when preview starts."
        );
        foreach (var name in RaidEditorAiView.CreationControls)
            _view.Visible(name, visible);
        if (!visible)
        {
            foreach (var name in RaidEditorAiView.InspectorGroups)
                _view.Visible(name, false);
            foreach (var name in RaidEditorAiView.InspectorFields)
                _view.Visible(name + "Group", false);
            RefreshAiRoutes();
            return;
        }

        var selected = AiSelected(out var kind);
        _aiSelectionKind = kind;
        _view.Text("LibraryHeading", "BROWSER / AI");
        _view.Text("Identity", selected.Valid ? selected.Id : "Select an encounter, spawn, patrol or waypoint");
        _view.Value("Name", selected.Valid ? selected.Name : "");
        var point = selected.Point;
        SetVectorFields("Position", point?.Position);
        SetVectorFields("Rotation", point?.Rotation);
        SetVectorFields("Size", (point as MapVolume)?.Size);
        _view.Value("Radius", ((point as MapVolume)?.Radius ?? 0).ToString("0.###", CultureInfo.InvariantCulture));

        var encounter = selected.Encounter;
        var trigger = encounter?.Trigger;
        var wave = selected.Wave;
        var roster = selected.Roster;
        var route = selected.Route;
        var isEncounter = kind == "enc" && encounter != null;
        var isWave = kind == "wave" && wave != null;
        var isRoster = kind == "roster" && roster != null;
        var isRoute = kind == "route" && route != null;
        var isWaypoint = kind == "waypoint" && route != null;
        var isTrigger = kind == "trigger" && trigger?.Volume != null;
        _view.Visible("AiTriggerSection", isEncounter);
        _view.Visible("AiWaveSection", isWave);
        _view.Visible("AiRosterSection", isRoster);
        _view.Visible("AiAssignmentSection", isRoster);
        _view.Visible("AiPatrolSection", isRoute || isWaypoint);
        _view.Visible("AiTriggerEventIdGroup", isEncounter && trigger?.Type == MapEncounterTrigger.Event);
        _view.Visible("AiTriggerZoneIdGroup", isEncounter && trigger?.Type == MapEncounterTrigger.PlayerEntry);
        _view.Visible("AiWaveDelaySecondsGroup", isWave);
        _view.Visible("AiWaveWaitPreviousGroup", isWave);
        _view.Visible("AiRosterRoleGroup", isRoster);
        _view.Visible("AiRosterDifficultyGroup", isRoster);
        _view.Visible("AiRosterCountGroup", isRoster);
        _view.Visible("AiRosterSquadIdGroup", isRoster);
        _view.Visible("AiRosterSpawnPointsGroup", false);
        _view.Visible("AiRosterPatrolRouteGroup", false);
        _view.Visible("AiPaceGroup", isRoute);
        _view.Visible("AiCompletionGroup", isRoute);
        _view.Visible("AiWaypointWaitSecondsGroup", isWaypoint);
        _view.Visible("AiTriggerGroup", isEncounter);
        _view.Visible("AiRosterSpawnNextGroup", isRoster);
        _view.Visible("AiRosterPatrolNextGroup", isRoster);
        _view.Visible("PositionGroup", point != null);
        _view.Visible("RotationGroup", point != null);
        _view.Visible("SizeGroup", isTrigger);
        _view.Visible("RadiusGroup", isTrigger);

        if (isEncounter)
        {
            _view.Caption("AiTrigger", "Trigger: " + Display(trigger?.Type, MapEncounterTrigger.MissionStart));
            _view.Value("AiTriggerEventId", trigger?.EventId ?? "");
            _view.Value("AiTriggerZoneId", trigger?.ZoneId ?? "");
        }
        if (isWave)
        {
            _view.Value("AiWaveDelaySeconds", wave!.DelaySeconds.ToString("0.###", CultureInfo.InvariantCulture));
            _view.Checked("AiWaveWaitPrevious", wave.WaitForPreviousWave);
        }
        if (isRoster)
        {
            SetAiChoice("AiRosterRole", AiRoleValues, roster!.Role);
            SetAiChoice("AiRosterDifficulty", AiDifficultyValues, roster.Difficulty);
            _view.Value("AiRosterCount", roster.Count.ToString(CultureInfo.InvariantCulture));
            _view.Value("AiRosterSquadId", roster.SquadId ?? "");
            _view.Value("AiRosterSpawnPoints", string.Join(", ", roster.SpawnPointIds ?? new()));
            _view.Value("AiRosterPatrolRoute", roster.PatrolRouteId ?? "");
            RefreshAiAssignments(roster);
        }
        if (isRoute)
        {
            SetAiChoice("AiPace", AiPaceValues, route!.Pace);
            SetAiChoice("AiCompletion", AiCompletionValues, route.Completion);
        }
        if (isWaypoint)
        {
            var index = route!.Waypoints.FindIndex(p => p.Id == selected.Waypoint!.Id);
            var wait = route.WaitSeconds != null && index >= 0 && index < route.WaitSeconds.Count ? route.WaitSeconds[index] : 0;
            _view.Value("AiWaypointWaitSeconds", wait.ToString("0.###", CultureInfo.InvariantCulture));
        }

        _view.Text("Details", selected.Valid ? AiDetails(selected) : "AI authoring and preview. Select a record to edit.");
        _view.Get<Button>("AiReset").interactable = AiPreviewBusy;
        _view.Get<Button>("AiSimulate").interactable = _aiPreview;
        _view.Windows.SetTooltip(
            "AiSimulate",
            "Select an encounter, wave or roster to simulate its trigger. A patrol route controls movement after spawning; it does not activate an encounter."
        );
        var editable = !AiPreviewBusy && _session?.Conflict == null && _session?.Definition != null && !_session.Previewing;
        foreach (
            var name in new[]
            {
                "AiEncounter",
                "AiWave",
                "AiRoster",
                "AiSpawn",
                "AiPatrol",
                "AiWaypoint",
                "AiTrigger",
                "Delete",
                "AiWaveWaitPrevious",
            }
        )
            _view.Get<Button>(name).interactable = editable;
        foreach (var id in new[] { "AiRosterRole", "AiRosterDifficulty", "AiPace", "AiCompletion" })
            _view.Get<EditorChoice>(id).interactable = editable;
        _view.Get<EditorChoice>("AiRosterSpawnNext").interactable = editable;
        _view.Get<EditorChoice>("AiRosterPatrolNext").interactable = editable;
        _view.Get<Button>("AiWave").interactable = editable && selected.Encounter != null;
        _view.Get<Button>("AiRoster").interactable = editable && selected.Wave != null;
        _view.Get<Button>("AiWaypoint").interactable = editable && selected.Route != null;
        _view.Windows.SetTooltip("AiWave", "Select an encounter in the tree, then add a wave.");
        _view.Windows.SetTooltip("AiRoster", "Select a wave in the tree, then add its bot roster.");
        _view.Windows.SetTooltip("AiWaypoint", "Select a patrol route in the tree, then place a waypoint.");
        foreach (
            var name in new[]
            {
                "AiTriggerEventId",
                "AiTriggerZoneId",
                "AiWaveDelaySeconds",
                "AiRosterCount",
                "AiRosterSquadId",
                "AiRosterSpawnPoints",
                "AiRosterPatrolRoute",
                "AiWaypointWaitSeconds",
            }
        )
            _view.Get<InputField>(name).interactable = editable;
        _view.Feedback(_session!.Status, _aiPreviewStatus, _notice);
        RefreshAiRoutes();
    }

    private void RefreshAiAssignments(MapEncounterRosterEntry roster)
    {
        _aiSpawnChoices.Clear();
        _aiPatrolChoices.Clear();
        var spawns = new List<EditorChoice.OptionData> { new("Add / remove spawn…") };
        var names = new List<string>();
        foreach (var point in Layout!.SpawnPoints)
        {
            _aiSpawnChoices.Add(point.Id);
            var assigned = roster.SpawnPointIds.Contains(point.Id);
            var label = (_aiSpawnChoices.Count) + ". " + point.Name;
            spawns.Add(new((assigned ? "✓ " : "+ ") + label));
            if (assigned)
                names.Add(label);
        }
        foreach (var id in roster.SpawnPointIds)
            if (!_aiSpawnChoices.Contains(id))
            {
                _aiSpawnChoices.Add(id);
                spawns.Add(new("Remove missing spawn: " + id));
                names.Add("Missing spawn: " + id);
            }
        _view!.SetDropdown("AiRosterSpawnNext", spawns, 0);
        _view.Text("AiAssignedSpawns", names.Count == 0 ? "No spawns assigned" : string.Join("\n", names));
        var patrols = new List<EditorChoice.OptionData> { new("Patrol: none") };
        _aiPatrolChoices.Add("");
        foreach (var route in Layout.PatrolRoutes)
        {
            _aiPatrolChoices.Add(route.Id);
            patrols.Add(new("Patrol: " + (_aiPatrolChoices.Count - 1) + ". " + route.Name));
        }
        var index = _aiPatrolChoices.IndexOf(roster.PatrolRouteId ?? "");
        if (index < 0)
        {
            index = _aiPatrolChoices.Count;
            _aiPatrolChoices.Add(roster.PatrolRouteId);
            patrols.Add(new("Missing patrol: " + roster.PatrolRouteId));
        }
        _view.SetDropdown("AiRosterPatrolNext", patrols, index);
    }

    private void SetVectorFields(string group, SpatialVector? vector)
    {
        var values = vector == null ? new[] { 0f, 0f, 0f } : new[] { vector.X, vector.Y, vector.Z };
        for (var i = 0; i < 3; i++)
            _view!.Value(group + "XYZ"[i], values[i].ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static string AiDetails(AiSelection selected)
    {
        var id = selected.Id;
        return selected.Kind switch
        {
            "enc" => "Encounter · "
                + Display(selected.Encounter?.Trigger?.Type, MapEncounterTrigger.MissionStart)
                + "\nFinite waves · preview only",
            "trigger" => "Trigger volume · independently authored\nNavMesh and standing clearance checked before preview",
            "wave" => "Wave · delay "
                + selected.Wave!.DelaySeconds.ToString("0.###", CultureInfo.InvariantCulture)
                + "s\nRoster entries use installed bot generation",
            "roster" => "Role: "
                + AiRoleDisplay(selected.Roster!.Role)
                + " · count "
                + selected.Roster.Count
                + "\nSupported roles: Scav, USEC, BEAR. Special and modded roles are unavailable.\nSAIN owns combat; patrols yield during combat/search/recovery",
            "route" => "Patrol · "
                + Display(selected.Route!.Completion, MapPatrolRoute.Loop)
                + " · "
                + Display(selected.Route.Pace, MapPatrolRoute.Walk)
                + "\nOrdered waypoints use the checkpoint route renderer",
            "waypoint" => "Waypoint · NavMesh checked before preview\n" + id,
            "spawn" => "Authored spawn reservation · NavMesh checked before preview\n" + id,
            _ => "AI authoring record",
        };
    }

    private void CycleAiTrigger()
    {
        var selected = AiSelected(out var kind);
        if (selected.Encounter == null || kind != "enc")
        {
            _notice = "Select an encounter before changing its activation trigger.";
            return;
        }
        var encounterId = selected.Encounter.Id;
        var current = Display(selected.Encounter.Trigger?.Type, MapEncounterTrigger.MissionStart);
        var next =
            current == MapEncounterTrigger.MissionStart ? MapEncounterTrigger.Event
            : current == MapEncounterTrigger.Event ? MapEncounterTrigger.PlayerEntry
            : MapEncounterTrigger.MissionStart;
        MapVolume? volume = null;
        if (next == MapEncounterTrigger.PlayerEntry && selected.Encounter.Trigger?.Volume == null)
        {
            try
            {
                volume = CaptureVolume("Encounter trigger");
            }
            catch (Exception error)
            {
                _notice = error.Message;
                return;
            }
        }
        EditAi(layout =>
        {
            var encounter = layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == encounterId);
            if (encounter == null)
                return;
            encounter.Trigger ??= new();
            encounter.Trigger.Type = next;
            if (next == MapEncounterTrigger.Event)
            {
                encounter.Trigger.EventId = string.IsNullOrWhiteSpace(encounter.Trigger.EventId) ? "manual" : encounter.Trigger.EventId;
                encounter.Trigger.ZoneId = "";
                encounter.Trigger.Volume = null;
            }
            else if (next == MapEncounterTrigger.PlayerEntry)
            {
                encounter.Trigger.EventId = "";
                encounter.Trigger.ZoneId = "";
                encounter.Trigger.Volume ??= volume;
            }
            else
            {
                encounter.Trigger.EventId = "";
                encounter.Trigger.ZoneId = "";
                encounter.Trigger.Volume = null;
            }
        });
        Refresh();
    }

    private void SimulateSelectedAiEvent()
    {
        if (!_aiPreview || _aiRuntime == null)
            return;
        var selected = AiSelected(out _);
        var trigger = selected.Encounter?.Trigger;
        if (trigger == null)
        {
            _notice = "Select an encounter, wave or roster and use Simulate. A patrol route alone does not activate bots.";
            return;
        }
        _notice = "Trigger simulated. Each encounter activates once per preview; Reset preview to run it again.";
        if (trigger.Type == MapEncounterTrigger.MissionStart)
        {
            SimulateAiStart();
            return;
        }
        if (trigger.Type == MapEncounterTrigger.Event)
        {
            if (string.IsNullOrWhiteSpace(trigger.EventId))
            {
                _notice = "Enter an event id before simulating this encounter.";
                return;
            }
            SimulateAiEvent(trigger.EventId);
            return;
        }
        // Player-entry triggers are exposed as an explicit preview event so
        // simulation never moves the editor player or advances campaign state.
        SimulateAiEntry(selected.Encounter!.Id);
    }

    private void EditAiTriggerText(string property, string value)
    {
        var selected = AiSelected(out var kind);
        if (kind != "enc" || selected.Encounter == null)
            return;
        var id = selected.Encounter.Id;
        EditAi(layout =>
        {
            var trigger = layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == id)?.Trigger;
            if (trigger == null)
                return;
            if (property == "EventId")
                trigger.EventId = value.Trim();
            else if (property == "ZoneId")
                trigger.ZoneId = value.Trim();
        });
    }

    private void EditAiWaveDelay(string value)
    {
        if (!TryFinite(value, out var delay) || delay < 0 || delay > 3600)
        {
            _notice = "Wave delay must be between 0 and 3600 seconds.";
            return;
        }
        var selected = AiSelected(out var kind);
        if (kind != "wave" || selected.Wave == null || selected.Encounter == null)
            return;
        var encounterId = selected.Encounter.Id;
        var waveId = selected.Wave.Id;
        EditAi(layout =>
        {
            var wave = Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == encounterId)?.Waves)
                .AsValueEnumerable()
                .FirstOrDefault(w => w.Id == waveId);
            if (wave != null)
                wave.DelaySeconds = delay;
        });
    }

    private void ToggleAiWaveWait()
    {
        var selected = AiSelected(out var kind);
        if (kind != "wave" || selected.Wave == null || selected.Encounter == null)
            return;
        var encounterId = selected.Encounter.Id;
        var waveId = selected.Wave.Id;
        EditAi(layout =>
        {
            var wave = Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == encounterId)?.Waves)
                .AsValueEnumerable()
                .FirstOrDefault(w => w.Id == waveId);
            if (
                wave != null
                && wave.Id != layout.Encounters.AsValueEnumerable().First(e => e.Id == encounterId).Waves.AsValueEnumerable().First().Id
            )
                wave.WaitForPreviousWave = !wave.WaitForPreviousWave;
        });
    }

    private static readonly string[] AiRoleValues = { "assault", "pmcUSEC", "pmcBEAR" };
    private static readonly string[] AiDifficultyValues = { "easy", "normal", "hard" };
    private static readonly string[] AiPaceValues = { MapPatrolRoute.Walk, MapPatrolRoute.Run };
    private static readonly string[] AiCompletionValues = { MapPatrolRoute.Loop, MapPatrolRoute.PingPong, MapPatrolRoute.Stop };

    private void SetAiChoice(string id, string[] values, string value)
    {
        var options = new List<EditorChoice.OptionData>();
        foreach (var option in values)
            options.Add(new EditorChoice.OptionData(id == "AiRosterRole" ? AiRoleDisplay(option) : option));
        var index = Array.IndexOf(values, value);
        if (index < 0)
        {
            index = options.Count;
            options.Add(new EditorChoice.OptionData(value + " (current)"));
        }
        _view!.SetDropdown(id, options, index);
    }

    private void SetAiRole(int index)
    {
        if (index < 0 || index >= AiRoleValues.Length)
            return;
        var selected = AiSelected(out var kind);
        if (kind != "roster" || selected.Roster == null || selected.Encounter == null || selected.Wave == null)
            return;
        var ids = (selected.Encounter.Id, selected.Wave.Id, selected.Roster.Id);
        EditAi(layout =>
        {
            var roster = Items(
                    Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == ids.Item1)?.Waves)
                        .AsValueEnumerable()
                        .FirstOrDefault(w => w.Id == ids.Item2)
                        ?.Roster
                )
                .AsValueEnumerable()
                .FirstOrDefault(r => r.Id == ids.Item3);
            if (roster == null)
                return;
            roster.Role = AiRoleValues[index];
        });
    }

    private void SetAiDifficulty(int index)
    {
        if (index < 0 || index >= AiDifficultyValues.Length)
            return;
        var selected = AiSelected(out var kind);
        if (kind != "roster" || selected.Roster == null || selected.Encounter == null || selected.Wave == null)
            return;
        var ids = (selected.Encounter.Id, selected.Wave.Id, selected.Roster.Id);
        EditAi(layout =>
        {
            var roster = Items(
                    Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == ids.Item1)?.Waves)
                        .AsValueEnumerable()
                        .FirstOrDefault(w => w.Id == ids.Item2)
                        ?.Roster
                )
                .AsValueEnumerable()
                .FirstOrDefault(r => r.Id == ids.Item3);
            if (roster == null)
                return;
            roster.Difficulty = AiDifficultyValues[index];
        });
    }

    private void EditAiRosterCount(string value)
    {
        if (
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count < 1
            || count > MapEncounterRules.MaxBotsPerWave
        )
        {
            _notice = "Roster count must be between 1 and 256.";
            return;
        }
        EditAiRosterText("Count", count.ToString(CultureInfo.InvariantCulture));
    }

    private void EditAiRosterText(string property, string value)
    {
        var selected = AiSelected(out var kind);
        if (kind != "roster" || selected.Roster == null || selected.Encounter == null || selected.Wave == null)
            return;
        var ids = (selected.Encounter.Id, selected.Wave.Id, selected.Roster.Id);
        EditAi(layout =>
        {
            var roster = Items(
                    Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == ids.Item1)?.Waves)
                        .AsValueEnumerable()
                        .FirstOrDefault(w => w.Id == ids.Item2)
                        ?.Roster
                )
                .AsValueEnumerable()
                .FirstOrDefault(r => r.Id == ids.Item3);
            if (roster == null)
                return;
            if (property == "SquadId")
                roster.SquadId = value.Trim();
            else if (property == "PatrolRouteId")
                roster.PatrolRouteId = value.Trim();
            else if (property == "Count" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                roster.Count = count;
        });
    }

    private void EditAiSpawnPoints(string value)
    {
        var selected = AiSelected(out var kind);
        if (kind != "roster" || selected.Roster == null || selected.Encounter == null || selected.Wave == null)
            return;
        var ids = (selected.Encounter.Id, selected.Wave.Id, selected.Roster.Id);
        var points = new List<string>();
        var pointIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in value.Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var id = point.Trim();
            if (id.Length > 0 && pointIds.Add(id))
                points.Add(id);
        }
        EditAi(layout =>
        {
            var roster = Items(
                    Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == ids.Item1)?.Waves)
                        .AsValueEnumerable()
                        .FirstOrDefault(w => w.Id == ids.Item2)
                        ?.Roster
                )
                .AsValueEnumerable()
                .FirstOrDefault(r => r.Id == ids.Item3);
            if (roster != null)
                roster.SpawnPointIds = points;
        });
    }

    private void CycleAiSpawnAssignment()
    {
        var selected = AiSelected(out var kind);
        if (kind != "roster" || selected.Roster == null || selected.Encounter == null || selected.Wave == null)
            return;
        var ids = (selected.Encounter.Id, selected.Wave.Id, selected.Roster.Id);
        EditAi(layout =>
        {
            var roster = Items(
                    Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == ids.Item1)?.Waves)
                        .AsValueEnumerable()
                        .FirstOrDefault(w => w.Id == ids.Item2)
                        ?.Roster
                )
                .AsValueEnumerable()
                .FirstOrDefault(r => r.Id == ids.Item3);
            var available = new List<string>();
            foreach (var point in Items(layout.SpawnPoints))
                if (point != null && point.Id.Length > 0)
                    available.Add(point.Id);
            if (roster == null || available.Count == 0)
            {
                _notice = "Add an authored AI spawn point before assigning one.";
                return;
            }
            roster.SpawnPointIds ??= new();
            var required = Math.Max(1, roster.Count);
            var next = available.AsValueEnumerable().FirstOrDefault(id => !ContainsOrdinal(roster.SpawnPointIds, id)) ?? "";
            if (roster.SpawnPointIds.Count < required)
            {
                if (next.Length == 0)
                {
                    _notice = "Add another authored AI spawn point before assigning this roster count.";
                    return;
                }
                roster.SpawnPointIds.Add(next);
                return;
            }

            // Keep one distinct authored anchor per bot. If every available
            // anchor is already assigned, rotate the existing reservation list
            // instead of duplicating an anchor and stacking bots on it.
            var replacement = available.AsValueEnumerable().FirstOrDefault(id => !ContainsAfterFirst(roster.SpawnPointIds, id)) ?? "";
            if (replacement.Length > 0)
            {
                roster.SpawnPointIds[0] = replacement;
            }
            else if (roster.SpawnPointIds.Count > 1)
            {
                var first = roster.SpawnPointIds[0];
                roster.SpawnPointIds.RemoveAt(0);
                roster.SpawnPointIds.Add(first);
            }
            else
            {
                _notice = "This roster already uses its only available authored spawn point.";
            }
        });
    }

    private void CycleAiPatrolAssignment()
    {
        var selected = AiSelected(out var kind);
        if (kind != "roster" || selected.Roster == null || selected.Encounter == null || selected.Wave == null)
            return;
        var ids = (selected.Encounter.Id, selected.Wave.Id, selected.Roster.Id);
        EditAi(layout =>
        {
            var roster = Items(
                    Items(layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == ids.Item1)?.Waves)
                        .AsValueEnumerable()
                        .FirstOrDefault(w => w.Id == ids.Item2)
                        ?.Roster
                )
                .AsValueEnumerable()
                .FirstOrDefault(r => r.Id == ids.Item3);
            var available = new List<string>();
            foreach (var patrol in Items(layout.PatrolRoutes))
                if (patrol != null && patrol.Id.Length > 0)
                    available.Add(patrol.Id);
            if (roster == null || available.Count == 0)
            {
                _notice = "Add a patrol route before assigning one.";
                return;
            }
            var index = available.IndexOf(roster.PatrolRouteId);
            roster.PatrolRouteId = available[(index + 1) % available.Count];
        });
    }

    private void SetAiPace(int index)
    {
        if (index < 0 || index >= AiPaceValues.Length)
            return;
        var selected = AiSelected(out var kind);
        if (kind != "route" || selected.Route == null)
            return;
        var id = selected.Route.Id;
        EditAi(layout =>
        {
            var route = layout.PatrolRoutes.AsValueEnumerable().FirstOrDefault(r => r.Id == id);
            if (route != null)
                route.Pace = AiPaceValues[index];
        });
    }

    private void SetAiCompletion(int index)
    {
        if (index < 0 || index >= AiCompletionValues.Length)
            return;
        var selected = AiSelected(out var kind);
        if (kind != "route" || selected.Route == null)
            return;
        var id = selected.Route.Id;
        var next = AiCompletionValues[index];
        if (!TryValidateAiRoute(selected.Route, route => route.Completion = next))
            return;
        EditAi(layout =>
        {
            var route = layout.PatrolRoutes.AsValueEnumerable().FirstOrDefault(r => r.Id == id);
            if (route == null)
                return;
            route.Completion = next;
        });
    }

    private void EditAiWaypointWait(string value)
    {
        if (!TryFinite(value, out var wait) || wait < 0 || wait > 3600)
        {
            _notice = "Waypoint wait must be between 0 and 3600 seconds.";
            return;
        }
        var selected = AiSelected(out var kind);
        if (kind != "waypoint" || selected.Route == null || selected.Waypoint == null)
            return;
        var routeId = selected.Route.Id;
        var waypointId = selected.Waypoint.Id;
        EditAi(layout =>
        {
            var route = layout.PatrolRoutes.AsValueEnumerable().FirstOrDefault(r => r.Id == routeId);
            if (route == null)
                return;
            var index = route.Waypoints.FindIndex(p => p.Id == waypointId);
            if (index < 0)
                return;
            route.WaitSeconds ??= new();
            while (route.WaitSeconds.Count < route.Waypoints.Count)
                route.WaitSeconds.Add(0);
            route.WaitSeconds[index] = wait;
        });
    }

    private static bool TryFinite(string value, out float result) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && float.IsFinite(result);

    private static bool ContainsOrdinal(List<string>? values, string value)
    {
        if (values == null)
            return false;
        foreach (var candidate in values)
            if (string.Equals(candidate, value, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool ContainsAfterFirst(List<string>? values, string value)
    {
        if (values == null)
            return false;
        for (var i = 1; i < values.Count; i++)
            if (string.Equals(values[i], value, StringComparison.Ordinal))
                return true;
        return false;
    }
}
