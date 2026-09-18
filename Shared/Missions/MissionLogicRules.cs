using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Missions;

public static class MissionLogicRules
{
    // Drafts may contain incomplete references while the author is still choosing them.
    // Structural bounds remain enforced on every save; publication and playtests use Errors.
    public static List<string> DraftErrors(MissionDefinition mission)
    {
        var errors = new List<string>();
        void Need(bool condition, string message)
        {
            if (!condition)
                errors.Add(message);
        }
        if (
            mission.Events == null
            || mission.Objectives == null
            || mission.Requirements == null
            || mission.Events.Any(e => e == null || e.Actions == null || e.Actions.Any(a => a == null))
            || mission.Objectives.Any(o => o == null || o.TargetIds == null)
            || mission.Requirements.Any(r => r == null || r.ObjectiveIds == null)
        )
            return new() { "Mission logic collections cannot be null or contain null records." };
        Need(
            mission.Events.Count <= 128 && mission.Objectives.Count <= 128 && mission.Requirements.Count <= 128,
            "Mission logic is limited to 128 events, objectives and requirements each."
        );
        var ids = mission.Events.Select(e => e.Id).Concat(mission.Objectives.Select(o => o.Id)).ToArray();
        Need(
            ids.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 120) && ids.Distinct().Count() == ids.Length,
            "Mission events and objectives need unique identities (up to 120 characters)."
        );
        Need(
            mission.Events.All(e => e.Actions.Count <= 32)
                && mission.Objectives.All(o => o.TargetIds.Count <= 128)
                && mission.Requirements.All(r => r.ObjectiveIds.Count <= 128),
            "Mission action and reference collections exceed their limits."
        );
        return errors;
    }

    public static List<string> Errors(MissionDefinition mission, MapLayout layout)
    {
        var errors = DraftErrors(mission);
        if (errors.Count > 0)
            return errors;
        void Need(bool condition, string message)
        {
            if (!condition)
                errors.Add(message);
        }
        var encounters = layout.Encounters.Select(e => e.Id).ToHashSet();
        var waves = layout.Encounters.SelectMany(e => e.Waves).Select(w => w.Id).ToHashSet();
        var rosters = layout.Encounters.SelectMany(e => e.Waves).SelectMany(w => w.Roster).ToArray();
        var rosterIds = rosters.Select(r => r.Id).ToHashSet();
        var squads = layout
            .Encounters.SelectMany(e =>
                e.Waves.SelectMany(w => w.Roster).Where(r => r.SquadId.Length > 0).Select(r => e.Id + ":" + r.SquadId)
            )
            .ToHashSet();
        var zones = layout
            .Checkpoints.Select(c => c.Id)
            .Concat(layout.Exit == null ? Array.Empty<string>() : new[] { layout.Exit.Id })
            .ToHashSet();
        var interactions = layout
            .Doors.Select(d => d.Id)
            .Concat(layout.Objects.Where(o => o.Container != null || SceneAssetRules.IsContainer(o)).Select(o => o.Id))
            .ToHashSet();
        var objectives = mission.Objectives.Select(o => o.Id).ToHashSet();
        var rules = mission.Events.Select(e => e.Id).ToHashSet();
        var timers = mission
            .Events.SelectMany(e => e.Actions)
            .Where(a => a.Type == MissionAction.Timer)
            .Select(a => a.TargetId)
            .ToHashSet();
        foreach (var objective in mission.Objectives)
        {
            Need(!string.IsNullOrWhiteSpace(objective.Name) && objective.Name.Length <= 120, "Objective name is required.");
            Need(
                objective.Type
                    is MissionObjective.Eliminate
                        or MissionObjective.Target
                        or MissionObjective.Survive
                        or MissionObjective.Defend
                        or MissionObjective.Protect,
                "Unknown mission objective type: " + objective.Type
            );
            var targets = objective.TargetKind switch
            {
                "Roster" => rosterIds,
                "Squad" => squads,
                "Encounter" => encounters,
                _ => new HashSet<string>(),
            };
            Need(
                objective.TargetIds.Count > 0
                    && objective.TargetIds.Count <= 128
                    && objective.TargetIds.Distinct().Count() == objective.TargetIds.Count
                    && objective.TargetIds.All(targets.Contains),
                "Choose valid, distinct objective targets: " + objective.Name
            );
            if (objective.Type is MissionObjective.Target or MissionObjective.Protect)
                Need(
                    objective.TargetKind == "Roster"
                        && objective.TargetIds.Count == 1
                        && rosters.Any(r => r.Id == objective.TargetIds[0] && r.Count == 1),
                    "Individual targets require one single-bot roster: " + objective.Name
                );
            if (objective.Type == MissionObjective.Survive)
                Need(objective.TargetKind == "Encounter", "Survive waves requires encounter targets.");
            if (objective.Type == MissionObjective.Defend)
                Need(
                    zones.Contains(objective.ZoneId)
                        && double.IsFinite(objective.Seconds)
                        && objective.Seconds > 0
                        && objective.Seconds <= 3600,
                    "Defend requires an authored checkpoint/exit volume and duration of 1–3600 seconds."
                );
            Need(
                objective.UntilEventId.Length == 0 || objective.Type == MissionObjective.Protect && rules.Contains(objective.UntilEventId),
                "Protection endpoint must reference a mission event."
            );
            Need(
                objective.OnStart
                    || mission.Events.SelectMany(e => e.Actions).Any(a => a.Type == MissionAction.Objective && a.TargetId == objective.Id),
                "Objective has no activation event: " + objective.Name
            );
        }
        foreach (var rule in mission.Events)
        {
            Need(
                !string.IsNullOrWhiteSpace(rule.Name) && rule.Name.Length <= 120 && rule.Actions.Count is > 0 and <= 32,
                "Event requires a name and 1–32 actions."
            );
            Need(
                rule.Source switch
                {
                    MissionSignals.Start => rule.SourceId.Length == 0,
                    MissionSignals.Checkpoint => layout.Checkpoints.Any(c => c.Id == rule.SourceId),
                    MissionSignals.Enter or MissionSignals.Leave => zones.Contains(rule.SourceId),
                    MissionSignals.Interaction => interactions.Contains(rule.SourceId),
                    MissionSignals.Timer => timers.Contains(rule.SourceId),
                    MissionSignals.Death => rosterIds.Contains(rule.SourceId),
                    MissionSignals.Wave => waves.Contains(rule.SourceId),
                    MissionSignals.Encounter => encounters.Contains(rule.SourceId),
                    MissionSignals.Complete or MissionSignals.Fail => objectives.Contains(rule.SourceId),
                    _ => false,
                },
                "Event source is unsupported or unresolved: " + rule.Name
            );
            foreach (var action in rule.Actions)
                Need(
                    action.Type switch
                    {
                        MissionAction.Encounter => layout.Encounters.Any(e =>
                            e.Id == action.TargetId && e.Trigger.Type == MapEncounterTrigger.Event
                        ),
                        MissionAction.Objective => objectives.Contains(action.TargetId),
                        MissionAction.Timer => !string.IsNullOrWhiteSpace(action.TargetId)
                            && action.TargetId.Length <= 120
                            && double.IsFinite(action.Seconds)
                            && action.Seconds > 0
                            && action.Seconds <= 3600,
                        _ => false,
                    },
                    "Event action is unsupported or unresolved: " + rule.Name
                );
        }
        foreach (var requirement in mission.Requirements)
        {
            Need(
                requirement.CheckpointId.Length == 0 || layout.Checkpoints.Any(c => c.Id == requirement.CheckpointId),
                "Objective gate references a missing checkpoint."
            );
            Need(requirement.ObjectiveIds.All(objectives.Contains), "Objective gate references a missing objective.");
            if (requirement.CheckpointId.Length > 0)
                Need(
                    !mission.Objectives.Any(o =>
                        requirement.ObjectiveIds.Contains(o.Id) && o.Type == MissionObjective.Protect && o.UntilEventId.Length == 0
                    ),
                    "Protect-until-exit cannot gate a checkpoint."
                );
        }
        // Include the dependencies through objectives, encounter completion, timers, and gated checkpoints.
        var graph = new Dictionary<string, HashSet<string>>();
        void Edge(string from, string to)
        {
            if (!graph.TryGetValue(from, out var links))
                graph[from] = links = new();
            links.Add(to);
        }
        string Source(MissionEventRule e) =>
            e.Source switch
            {
                MissionSignals.Complete or MissionSignals.Fail => "objective:" + e.SourceId,
                MissionSignals.Encounter => "encounter:" + e.SourceId,
                MissionSignals.Timer => "timer:" + e.SourceId,
                MissionSignals.Checkpoint => "checkpoint:" + e.SourceId,
                MissionSignals.Death => "encounter:"
                    + layout.Encounters.FirstOrDefault(c => c.Waves.Any(w => w.Roster.Any(r => r.Id == e.SourceId)))?.Id,
                MissionSignals.Wave => "encounter:" + layout.Encounters.FirstOrDefault(c => c.Waves.Any(w => w.Id == e.SourceId))?.Id,
                _ => "source:" + e.Source + ":" + e.SourceId,
            };
        foreach (var e in mission.Events)
        {
            Edge(Source(e), "rule:" + e.Id);
            foreach (var a in e.Actions)
                Edge(
                    "rule:" + e.Id,
                    (
                        a.Type == MissionAction.Encounter ? "encounter:"
                        : a.Type == MissionAction.Objective ? "objective:"
                        : "timer:"
                    ) + a.TargetId
                );
        }
        foreach (var o in mission.Objectives)
        {
            foreach (
                var encounter in layout.Encounters.Where(e =>
                    e.Waves.Any(w =>
                        w.Roster.Any(r =>
                            MissionLogic.Matches(
                                o,
                                new MissionActor
                                {
                                    EncounterId = e.Id,
                                    RosterId = r.Id,
                                    SquadId = r.SquadId,
                                }
                            )
                        )
                    )
                )
            )
                Edge("encounter:" + encounter.Id, "objective:" + o.Id);
            if (o.UntilEventId.Length > 0)
                Edge("rule:" + o.UntilEventId, "objective:" + o.Id);
        }
        foreach (var r in mission.Requirements.Where(r => r.CheckpointId.Length > 0))
        foreach (var id in r.ObjectiveIds)
            Edge("objective:" + id, "checkpoint:" + r.CheckpointId);
        var visiting = new HashSet<string>();
        var done = new HashSet<string>();
        bool Cycle(string node)
        {
            if (done.Contains(node))
                return false;
            if (!visiting.Add(node))
                return true;
            if (graph.TryGetValue(node, out var links) && links.Any(Cycle))
                return true;
            visiting.Remove(node);
            done.Add(node);
            return false;
        }
        Need(!graph.Keys.ToArray().Any(Cycle), "Mission events/objectives contain a circular dependency.");
        return errors;
    }
}
