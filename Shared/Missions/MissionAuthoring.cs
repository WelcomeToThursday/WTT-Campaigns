using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Missions;

public static class MissionAuthoring
{
    public static readonly string[] ObjectiveTypes =
    {
        MissionObjective.Eliminate,
        MissionObjective.Target,
        MissionObjective.Survive,
        MissionObjective.Defend,
        MissionObjective.Protect,
    };
    public static readonly string[] Sources =
    {
        MissionSignals.Start,
        MissionSignals.Checkpoint,
        MissionSignals.Enter,
        MissionSignals.Leave,
        MissionSignals.Interaction,
        MissionSignals.Timer,
        MissionSignals.Death,
        MissionSignals.Wave,
        MissionSignals.Encounter,
        MissionSignals.Complete,
        MissionSignals.Fail,
    };

    public static string Label(string text) => System.Text.RegularExpressions.Regex.Replace(text, "([a-z])([A-Z])", "$1 $2");

    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 24);

    public static IEnumerable<(string Id, string Name)> Targets(MapLayout layout, string kind) =>
        kind switch
        {
            "Roster" => layout.Encounters.SelectMany(e =>
                e.Waves.SelectMany(w => w.Roster.Select(r => (r.Id, e.Name + " / " + w.Name + " / " + r.Role + " × " + r.Count)))
            ),
            "Squad" => layout
                .Encounters.SelectMany(e =>
                    e.Waves.SelectMany(w =>
                        w.Roster.Where(r => r.SquadId.Length > 0).Select(r => (e.Id + ":" + r.SquadId, e.Name + " / " + r.SquadId))
                    )
                )
                .Distinct(),
            _ => layout.Encounters.Select(e => (e.Id, e.Name)),
        };

    public static IEnumerable<(string Id, string Name)> SourceTargets(MissionDefinition mission, MapLayout layout, string source) =>
        source switch
        {
            MissionSignals.Checkpoint => layout.Checkpoints.Select(c => (c.Id, c.Name)),
            MissionSignals.Enter or MissionSignals.Leave or MissionSignals.Sample => layout
                .Checkpoints.Concat(layout.Exit == null ? Array.Empty<MapVolume>() : new[] { layout.Exit })
                .Select(c => (c.Id, c.Name)),
            MissionSignals.Complete or MissionSignals.Fail => mission.Objectives.Select(o => (o.Id, o.Name)),
            MissionSignals.Death => Targets(layout, "Roster"),
            MissionSignals.Encounter => Targets(layout, "Encounter"),
            MissionSignals.Wave => layout.Encounters.SelectMany(e => e.Waves.Select(w => (w.Id, e.Name + " / " + w.Name))),
            MissionSignals.Timer => mission
                .Events.SelectMany(e => e.Actions)
                .Where(a => a.Type == MissionAction.Timer)
                .Select(a => (a.TargetId, a.TargetId))
                .Distinct(),
            MissionSignals.Interaction => layout
                .Doors.Select(d => (d.Id, d.Name))
                .Concat(layout.Objects.Where(o => o.Container != null || SceneAssetRules.IsContainer(o)).Select(o => (o.Id, o.Name))),
            _ => Array.Empty<(string, string)>(),
        };

    public static IEnumerable<(string Id, string Name)> ActionTargets(MissionDefinition mission, MapLayout layout, string type) =>
        type == MissionAction.Objective
            ? mission.Objectives.Select(o => (o.Id, o.Name))
            : layout.Encounters.Where(e => e.Trigger.Type == MapEncounterTrigger.Event).Select(e => (e.Id, e.Name));

    public static void Remap(MissionDefinition mission, IReadOnlyDictionary<string, string> ids)
    {
        string Map(string id) => ids.TryGetValue(id, out var replacement) ? replacement : id;
        foreach (var rule in mission.Events)
        {
            rule.Id = Map(rule.Id);
            rule.SourceId = Map(rule.SourceId);
            foreach (var action in rule.Actions)
                action.TargetId = Map(action.TargetId);
        }
        foreach (var objective in mission.Objectives)
        {
            objective.Id = Map(objective.Id);
            objective.ZoneId = Map(objective.ZoneId);
            objective.UntilEventId = Map(objective.UntilEventId);
            objective.TargetIds = objective
                .TargetIds.Select(id =>
                    objective.TargetKind == "Squad" && id.Contains(':')
                        ? Map(id.Substring(0, id.IndexOf(':'))) + id.Substring(id.IndexOf(':'))
                        : Map(id)
                )
                .ToList();
        }
        foreach (var gate in mission.Requirements)
        {
            gate.CheckpointId = Map(gate.CheckpointId);
            gate.ObjectiveIds = gate.ObjectiveIds.Select(Map).ToList();
        }
    }
}
