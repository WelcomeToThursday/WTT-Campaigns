using System.Globalization;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorAiController
{
    private string _aiSelectionKind = "";

    internal bool AiWorkspace => _context.ToolId == "AI";

    private bool _inspectAiNavigation;

    private readonly List<string> _aiSpawnChoices = new();

    private readonly List<string> _aiPatrolChoices = new();

    private void EditAi(Action<MapLayout> edit)
    {
        if (
            _context.Session?.Definition == null
            || _context.AiPreviewBusy
            || _context.Session.Previewing
            || !AiWorkspace
            || _context.LayoutId.Length == 0
        )
            return;
        _context.Session.Edit(definition =>
        {
            var layout = definition.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == _context.LayoutId);
            if (layout == null)
                throw new InvalidOperationException("Select a mission layout before authoring AI encounters.");
            edit(layout);
        });
        _context.LibraryKey = "";
        _context.Refresh();
    }

    private static IEnumerable<T> Items<T>(IEnumerable<T>? values) => values ?? Array.Empty<T>();

    internal EditorAiSelection AiSelected(out string kind)
    {
        kind = "";
        if (_context.Layout == null || !EditorAiSelection.TryResolve(_context.Layout, _context.SelectionId, out var selection))
            return default;
        kind = selection.Kind;
        return selection;
    }

    internal SpatialCapture? AiSelectedPoint()
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
        if (_context.Session?.Definition == null)
            return new List<string>();
        var layout = _context.Session.Definition.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == _context.LayoutId);
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
        _context.Notice = "Edit rejected: " + error;
        return false;
    }

    private bool TryValidateSelectedAiWaypoint(string selectedId, Action<SpatialCapture> edit)
    {
        if (
            _context.Layout == null
            || !EditorAiSelection.TryResolve(_context.Layout, selectedId, out var selected)
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
        if (_context.Layout == null)
        {
            _context.Notice = "Select a layout before adding an encounter.";
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
        _context.SelectionId = "enc:" + encounterId;
        _context.Refresh();
    }

    private void AddAiWave()
    {
        var selected = AiSelected(out var kind);
        if (!selected.Valid || selected.Encounter == null || kind != "enc")
        {
            _context.Notice = "Select an encounter before adding a wave.";
            return;
        }
        var encounterId = selected.Encounter.Id;
        var id = RaidEditorAiContracts.Id();
        EditAi(layout =>
        {
            var encounter = layout.Encounters.AsValueEnumerable().FirstOrDefault(e => e.Id == encounterId);
            encounter?.Waves.Add(new MapEncounterWave { Id = id, Name = "Wave" });
        });
        _context.SelectionId = "wave:" + encounterId + ":" + id;
        _context.Refresh();
    }

    private void AddAiRoster()
    {
        var selected = AiSelected(out var kind);
        var encounter = selected.Encounter;
        var wave = kind == "enc" ? Items(encounter?.Waves).AsValueEnumerable().FirstOrDefault() : selected.Wave;
        if (!selected.Valid || encounter == null || wave == null || kind is not ("enc" or "wave" or "roster"))
        {
            _context.Notice = "Select an encounter or wave before adding a roster entry.";
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
        _context.SelectionId = "roster:" + encounterId + ":" + waveId + ":" + id;
        _context.Refresh();
    }

    internal bool TryAiPlacement(out Vector3 position, out string scene)
    {
        position = default;
        scene = "";
        if (!_context.IsOpen || !_context.Camera || !_context.TryRouteFloor(_context.CameraPosition, 20, out var floor))
        {
            _context.Notice = "Move the editor camera above a floor within 20 metres to place an AI marker.";
            return false;
        }
        position = floor.point;
        scene = floor.transform.gameObject.scene.name;
        if (!RaidEditorAiContracts.TryNav(position, out var safe) || (safe - position).sqrMagnitude > .2f * .2f)
        {
            _context.Notice = "Placement rejected: choose a clear NavMesh standing area.";
            return false;
        }
        position = safe;
        return true;
    }

    private void AddAiSpawn()
    {
        if (_context.Layout == null || !TryAiPlacement(out var position, out var scene))
            return;
        var id = RaidEditorAiContracts.Id();
        EditAi(layout =>
            layout.SpawnPoints.Add(
                new SpatialCapture
                {
                    Id = id,
                    Name = "New spawn point",
                    Location = _context.Session!.Location,
                    Scene = scene,
                    Position = ZoneRuntime.Vector(position),
                    Rotation = new SpatialVector { Y = _context.CameraRotation.eulerAngles.y },
                }
            )
        );
        _context.SelectionId = "spawn:" + id;
        _context.Refresh();
    }

    private void AddAiPatrol()
    {
        if (_context.Layout == null)
        {
            _context.Notice = "Select a layout before adding a patrol route.";
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
        _context.SelectionId = "route:" + id;
        _context.Refresh();
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
            _context.Notice = "Select a patrol route, then place its waypoint on the NavMesh.";
            return;
        }
        var routeId = route.Id;
        var id = RaidEditorAiContracts.Id();
        var waypoint = new SpatialCapture
        {
            Id = id,
            Name = "Waypoint " + ((route.Waypoints?.Count ?? 0) + 1),
            Location = _context.Session!.Location,
            Scene = scene,
            Position = ZoneRuntime.Vector(position),
            Rotation = new SpatialVector { Y = _context.CameraRotation.eulerAngles.y },
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
        _context.SelectionId = "waypoint:" + routeId + ":" + id;
        _context.Refresh();
    }

    internal void EditAiName(string value)
    {
        var selectedId = _context.SelectionId;
        EditAi(layout =>
        {
            if (!EditorAiSelection.TryResolve(layout, selectedId, out var selected))
                return;
            selected.Rename(value);
        });
    }

    internal void EditAiPoint(SpatialCapture point)
    {
        var selectedId = _context.SelectionId;
        if (
            point.Position == null
            || !RaidEditorAiContracts.TryNav(ZoneRuntime.Vector(point.Position), out var safe)
            || (safe - ZoneRuntime.Vector(point.Position)).sqrMagnitude > .001f
        )
        {
            _context.Notice = "Position rejected: it is not on a clear NavMesh standing area.";
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
            if (!EditorAiSelection.TryResolve(layout, selectedId, out var selected) || selected.Point == null)
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

    internal void DuplicateAiSelection()
    {
        var selected = AiSelected(out var kind);
        var sourceId = selected.Id;
        if (!selected.Valid || kind is not ("spawn" or "route" or "enc"))
        {
            _context.Notice = "Select an encounter, spawn point or patrol route to duplicate it.";
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
        _context.SelectionId = kind + ":" + id;
        _context.Refresh();
    }

    internal void EditAiVector(string group, int axis, float value)
    {
        var selectedId = _context.SelectionId;
        if (
            group == "Position"
            && _context.Layout != null
            && EditorAiSelection.TryResolve(_context.Layout, selectedId, out var selected)
            && selected.Point != null
            && selected.Point.Position != null
        )
        {
            var proposed = RaidEditorSession.Copy(selected.Point.Position);
            SetAxis(proposed, axis, value);
            var proposedWorld = ZoneRuntime.Vector(proposed);
            if (!RaidEditorAiContracts.TryNav(proposedWorld, out var safe) || (safe - proposedWorld).sqrMagnitude > .001f)
            {
                _context.Notice = "Position rejected: it is not on a clear NavMesh standing area.";
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
            if (!EditorAiSelection.TryResolve(layout, selectedId, out var selected) || selected.Point == null)
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

    internal void EditAiRadius(float value)
    {
        if (value <= 0 || !float.IsFinite(value))
            return;
        var selectedId = _context.SelectionId;
        EditAi(layout =>
        {
            if (EditorAiSelection.TryResolve(layout, selectedId, out var selected) && selected.Point is MapVolume volume)
                volume.Radius = value;
        });
    }

    internal void DeleteAiSelection()
    {
        var selected = AiSelected(out var kind);
        var parts = _context.SelectionId.Split(':');
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
        _context.SelectionId = "";
        _context.Refresh();
    }

    internal bool AiAcceptPreview(SpatialCapture point, SpatialCapture before)
    {
        if (!AiWorkspace || point.Position == null)
            return true;
        var proposed = ZoneRuntime.Vector(point.Position);
        if (!RaidEditorAiContracts.TryNav(proposed, out var safe) || (safe - proposed).sqrMagnitude > .001f)
        {
            _context.RestorePoint(point, before);
            _context.Notice = "Drag rejected: the AI position must remain on a clear NavMesh standing area.";
            return false;
        }
        if (
            _context.Layout != null
            && EditorAiSelection.TryResolve(_context.Layout, _context.SelectionId, out var selected)
            && selected.Kind == "waypoint"
            && selected.Route != null
        )
        {
            var error = RaidEditorAiContracts.RouteError(selected.Route);
            if (error.Length > 0)
            {
                _context.RestorePoint(point, before);
                _context.Notice = "Drag rejected: " + error;
                return false;
            }
        }
        return true;
    }

    private void CycleAiTrigger()
    {
        var selected = AiSelected(out var kind);
        if (selected.Encounter == null || kind != "enc")
        {
            _context.Notice = "Select an encounter before changing its activation trigger.";
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
                volume = _context.CaptureVolume("Encounter trigger");
            }
            catch (Exception error)
            {
                _context.Notice = error.Message;
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
        _context.Refresh();
    }

    private void SimulateSelectedAiEvent()
    {
        if (!_context.AiPreview || !_context.AiRuntimeAvailable)
            return;
        var selected = AiSelected(out _);
        var trigger = selected.Encounter?.Trigger;
        if (trigger == null)
        {
            _context.Notice = "Select an encounter, wave or roster and use Simulate. A patrol route alone does not activate bots.";
            return;
        }
        _context.Notice = "Trigger simulated. Each encounter activates once per preview; Reset preview to run it again.";
        if (trigger.Type == MapEncounterTrigger.MissionStart)
        {
            _context.SimulateAiStart();
            return;
        }
        if (trigger.Type == MapEncounterTrigger.Event)
        {
            if (string.IsNullOrWhiteSpace(trigger.EventId))
            {
                _context.Notice = "Enter an event id before simulating this encounter.";
                return;
            }
            _context.SimulateAiEvent(trigger.EventId);
            return;
        }
        // Player-entry triggers are exposed as an explicit preview event so
        // simulation never moves the editor player or advances campaign state.
        _context.SimulateAiEntry(selected.Encounter!.Id);
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
            _context.Notice = "Wave delay must be between 0 and 3600 seconds.";
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
            _context.Notice = "Roster count must be between 1 and 256.";
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
                _context.Notice = "Add an authored AI spawn point before assigning one.";
                return;
            }
            roster.SpawnPointIds ??= new();
            var required = Math.Max(1, roster.Count);
            var next = available.AsValueEnumerable().FirstOrDefault(id => !ContainsOrdinal(roster.SpawnPointIds, id)) ?? "";
            if (roster.SpawnPointIds.Count < required)
            {
                if (next.Length == 0)
                {
                    _context.Notice = "Add another authored AI spawn point before assigning this roster count.";
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
                _context.Notice = "This roster already uses its only available authored spawn point.";
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
                _context.Notice = "Add a patrol route before assigning one.";
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
            _context.Notice = "Waypoint wait must be between 0 and 3600 seconds.";
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
