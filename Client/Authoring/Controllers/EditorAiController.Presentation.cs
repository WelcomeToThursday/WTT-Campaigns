using System.Globalization;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using Button = WTT.Campaigns.Client.Authoring.Views.EditorButton;
using InputField = WTT.Campaigns.Client.Authoring.Views.EditorInput;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorAiController
{
    internal void Bind(RaidEditorView view)
    {
        _ = new MissionEditorPanel(view, view.ElementForTool("AI", "AiTools"), () => _context.Session);
        RaidEditorAiContracts.LayoutProvider = _layoutProvider;
        view.Button("AiEncounter", AddAiEncounter);
        view.Button("AiWave", AddAiWave);
        view.Button("AiRoster", AddAiRoster);
        view.Button("AiSpawn", AddAiSpawn);
        view.Button("AiPatrol", AddAiPatrol);
        view.Button("AiWaypoint", AddAiWaypoint);
        view.Button("AiWaypointInsert", () => AddAiWaypoint(true));
        view.Button("AiWaypointEarlier", () => MoveAiWaypoint(-1));
        view.Button("AiWaypointLater", () => MoveAiWaypoint(1));
        view.Button("AiRouteReverse", ReverseAiRoute);
        view.Button("AiTrigger", CycleAiTrigger);
        view.Button("AiObserve", () => _context.BeginAiPreview(false));
        view.Dropdown(
            "AiPlaytestGear",
            i =>
            {
                if (!_context.AiPreviewBusy)
                    _context.AiUseProfileKit = i == 1;
                _context.Refresh();
            }
        );
        view.Button("AiPlaytest", () => _context.BeginAiPreview(true));
        view.Button("TestCheckpoints", _context.TestEditorCheckpoints);
        view.Button("AiReset", _context.EndAiPreview);
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

    private static string Display(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string AiRoleDisplay(string? role) =>
        role switch
        {
            "assault" => "Scav",
            "pmcUSEC" => "USEC",
            "pmcBEAR" => "BEAR",
            null or "" => "Unsupported",
            _ => "Unsupported · " + role,
        };

    internal void RefreshAiWorkspace()
    {
        if (_context.View?.Valid != true)
            return;
        var visible = AiWorkspace;
        _context.View.Visible("AiTools", visible);
        _context.View.Visible("AiToolsScroll", visible);
        _context.View.InspectNavigation(visible && _inspectAiNavigation);
        _context.View.Caption("AiNavigation", "Inspect navigation: " + (_inspectAiNavigation ? "on" : "off"));
        _context.View.Windows.SetTooltip(
            "AiNavigation",
            "Select a patrol or waypoint for directed path details, or a spawn for nearby navigation samples, authored cuts, and its core connection."
        );
        foreach (var name in RaidEditorAiView.CreationControls)
            _context.View.Visible(name, visible);
        if (!visible)
        {
            foreach (var name in RaidEditorAiView.InspectorGroups)
                _context.View.Visible(name, false);
            foreach (var name in RaidEditorAiView.InspectorFields)
                _context.View.Visible(name + "Group", false);
            RefreshAiRoutes();
            return;
        }

        var selected = AiSelected(out var kind);
        _aiSelectionKind = kind;
        _context.View.Text("LibraryHeading", "BROWSER / AI");
        _context.View.Text("Identity", selected.Valid ? selected.Id : "Select an encounter, spawn, patrol or waypoint");
        _context.View.Value("Name", selected.Valid ? selected.Name : "");
        var point = selected.Point;
        SetVectorFields("Position", point?.Position);
        SetVectorFields("Rotation", point?.Rotation);
        SetVectorFields("Size", (point as MapVolume)?.Size);
        _context.View.Value("Radius", ((point as MapVolume)?.Radius ?? 0).ToString("0.###", CultureInfo.InvariantCulture));

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
        _context.View.Visible("AiTriggerSection", isEncounter);
        _context.View.Visible("AiWaveSection", isWave);
        _context.View.Visible("AiRosterSection", isRoster);
        _context.View.Visible("AiAssignmentSection", isRoster);
        _context.View.Visible("AiPatrolSection", isRoute || isWaypoint);
        _context.View.Visible("AiTriggerEventIdGroup", isEncounter && trigger?.Type == MapEncounterTrigger.Event);
        _context.View.Visible("AiTriggerZoneIdGroup", isEncounter && trigger?.Type == MapEncounterTrigger.PlayerEntry);
        _context.View.Visible("AiWaveDelaySecondsGroup", isWave);
        _context.View.Visible("AiWaveWaitPreviousGroup", isWave);
        _context.View.Visible("AiRosterRoleGroup", isRoster);
        _context.View.Visible("AiRosterDifficultyGroup", isRoster);
        _context.View.Visible("AiRosterCountGroup", isRoster);
        _context.View.Visible("AiRosterSquadIdGroup", isRoster);
        _context.View.Visible("AiRosterSpawnPointsGroup", false);
        _context.View.Visible("AiRosterPatrolRouteGroup", false);
        _context.View.Visible("AiPaceGroup", isRoute);
        _context.View.Visible("AiCompletionGroup", isRoute);
        _context.View.Visible("AiWaypointWaitSecondsGroup", isWaypoint);
        _context.View.Visible("AiTriggerGroup", isEncounter);
        _context.View.Visible("AiRosterSpawnNextGroup", isRoster);
        _context.View.Visible("AiRosterPatrolNextGroup", isRoster);
        _context.View.Visible("PositionGroup", point != null);
        _context.View.Visible("RotationGroup", point != null);
        _context.View.Visible("SizeGroup", isTrigger);
        _context.View.Visible("RadiusGroup", isTrigger);

        if (isEncounter)
        {
            _context.View.Caption("AiTrigger", "Trigger: " + Display(trigger?.Type, MapEncounterTrigger.MissionStart));
            _context.View.Value("AiTriggerEventId", trigger?.EventId ?? "");
            _context.View.Value("AiTriggerZoneId", trigger?.ZoneId ?? "");
        }
        if (isWave)
        {
            _context.View.Value("AiWaveDelaySeconds", wave!.DelaySeconds.ToString("0.###", CultureInfo.InvariantCulture));
            _context.View.Checked("AiWaveWaitPrevious", wave.WaitForPreviousWave);
        }
        if (isRoster)
        {
            SetAiChoice("AiRosterRole", AiRoleValues, roster!.Role);
            SetAiChoice("AiRosterDifficulty", AiDifficultyValues, roster.Difficulty);
            _context.View.Value("AiRosterCount", roster.Count.ToString(CultureInfo.InvariantCulture));
            _context.View.Value("AiRosterSquadId", roster.SquadId ?? "");
            _context.View.Value("AiRosterSpawnPoints", string.Join(", ", roster.SpawnPointIds ?? new()));
            _context.View.Value("AiRosterPatrolRoute", roster.PatrolRouteId ?? "");
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
            _context.View.Value("AiWaypointWaitSeconds", wait.ToString("0.###", CultureInfo.InvariantCulture));
        }

        _context.View.Text("Details", selected.Valid ? AiDetails(selected) : "AI authoring and preview. Select a record to edit.");
        _context.View.Get<Button>("AiReset").interactable = _context.AiPreviewBusy;
        _context.View.Get<Button>("AiSimulate").interactable = _context.AiPreview;
        _context.View.Windows.SetTooltip(
            "AiSimulate",
            "Select an encounter, wave or roster to simulate its trigger. A patrol route controls movement after spawning; it does not activate an encounter."
        );
        var editable =
            !_context.AiPreviewBusy
            && _context.Session?.Conflict == null
            && _context.Session?.Definition != null
            && !_context.Session.Previewing;
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
            _context.View.Get<Button>(name).interactable = editable;
        foreach (var id in new[] { "AiRosterRole", "AiRosterDifficulty", "AiPace", "AiCompletion" })
            _context.View.Get<EditorChoice>(id).interactable = editable;
        _context.View.Get<EditorChoice>("AiRosterSpawnNext").interactable = editable;
        _context.View.Get<EditorChoice>("AiRosterPatrolNext").interactable = editable;
        _context.View.Get<Button>("AiWave").interactable = editable && selected.Encounter != null;
        _context.View.Get<Button>("AiRoster").interactable = editable && selected.Wave != null;
        _context.View.Get<Button>("AiWaypoint").interactable = editable && selected.Route != null;
        var waypointIndex = isWaypoint ? route!.Waypoints.FindIndex(p => p.Id == selected.Waypoint?.Id) : -1;
        _context.View.Get<Button>("AiWaypointInsert").interactable = editable && waypointIndex >= 0;
        _context.View.Get<Button>("AiWaypointEarlier").interactable = editable && waypointIndex > 0;
        _context.View.Get<Button>("AiWaypointLater").interactable = editable && waypointIndex >= 0 && waypointIndex < route!.Waypoints.Count - 1;
        _context.View.Get<Button>("AiRouteReverse").interactable = editable && route?.Waypoints.Count > 1;
        _context.View.Windows.SetTooltip("AiWaypointInsert", "Insert a zero-wait waypoint after the selection, at the floor beneath the camera.");
        _context.View.Windows.SetTooltip("AiWaypointEarlier", "Move this waypoint earlier, keeping its wait and identity.");
        _context.View.Windows.SetTooltip("AiWaypointLater", "Move this waypoint later, keeping its wait and identity.");
        _context.View.Windows.SetTooltip("AiRouteReverse", "Reverse waypoint order and their waits. Completion mode stays unchanged.");
        _context.View.Windows.SetTooltip("AiWave", "Select an encounter in the tree, then add a wave.");
        _context.View.Windows.SetTooltip("AiRoster", "Select a wave in the tree, then add its bot roster.");
        _context.View.Windows.SetTooltip("AiWaypoint", "Select a patrol route in the tree, then place a waypoint.");
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
            _context.View.Get<InputField>(name).interactable = editable;
        _context.View.Feedback(_context.Session!.Status, _context.AiPreviewStatus);
        RefreshAiRoutes();
    }

    private void RefreshAiAssignments(MapEncounterRosterEntry roster)
    {
        _aiSpawnChoices.Clear();
        _aiPatrolChoices.Clear();
        var spawns = new List<EditorChoice.OptionData> { new("Add / remove spawn…") };
        var names = new List<string>();
        foreach (var point in _context.Layout!.SpawnPoints)
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
        _context.View!.SetDropdown("AiRosterSpawnNext", spawns, 0);
        _context.View.Text("AiAssignedSpawns", names.Count == 0 ? "No spawns assigned" : string.Join("\n", names));
        var patrols = new List<EditorChoice.OptionData> { new("Patrol: none") };
        _aiPatrolChoices.Add("");
        foreach (var route in _context.Layout.PatrolRoutes)
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
        _context.View.SetDropdown("AiRosterPatrolNext", patrols, index);
    }

    private void SetVectorFields(string group, SpatialVector? vector)
    {
        var values = vector == null ? new[] { 0f, 0f, 0f } : new[] { vector.X, vector.Y, vector.Z };
        for (var i = 0; i < 3; i++)
            _context.View!.Value(group + "XYZ"[i], values[i].ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static string AiDetails(EditorAiSelection selected)
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
        _context.View!.SetDropdown(id, options, index);
    }
}
