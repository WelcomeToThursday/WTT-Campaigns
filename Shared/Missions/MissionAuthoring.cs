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

    // Public streaming contracts use native iterators: ZLinq's net10 enumerators cannot
    // be stored in the state machines emitted by our netstandard2.1 compilation.
    public static IEnumerable<(string Id, string Name)> Targets(MapLayout layout, string kind)
    {
        var squads = new HashSet<(string, string)>();
        foreach (var encounter in layout.Encounters)
        {
            if (kind is not ("Roster" or "Squad"))
            {
                yield return (encounter.Id, encounter.Name);
                continue;
            }
            foreach (var wave in encounter.Waves)
            foreach (var entry in wave.Roster)
            {
                if (kind == "Roster")
                    yield return (entry.Id, encounter.Name + " / " + wave.Name + " / " + entry.Role + " × " + entry.Count);
                else if (entry.SquadId.Length > 0)
                {
                    var squad = (encounter.Id + ":" + entry.SquadId, encounter.Name + " / " + entry.SquadId);
                    if (squads.Add(squad))
                        yield return squad;
                }
            }
        }
    }

    public static IEnumerable<(string Id, string Name)> SourceTargets(MissionDefinition mission, MapLayout layout, string source)
    {
        switch (source)
        {
            case MissionSignals.Checkpoint:
            case MissionSignals.Enter:
            case MissionSignals.Leave:
            case MissionSignals.Sample:
                foreach (var checkpoint in layout.Checkpoints)
                    yield return (checkpoint.Id, checkpoint.Name);
                if (source != MissionSignals.Checkpoint && layout.Exit != null)
                    yield return (layout.Exit.Id, layout.Exit.Name);
                break;
            case MissionSignals.Complete:
            case MissionSignals.Fail:
                foreach (var objective in mission.Objectives)
                    yield return (objective.Id, objective.Name);
                break;
            case MissionSignals.Death:
            case MissionSignals.Encounter:
                foreach (var target in Targets(layout, source == MissionSignals.Death ? "Roster" : "Encounter"))
                    yield return target;
                break;
            case MissionSignals.Wave:
                foreach (var encounter in layout.Encounters)
                foreach (var wave in encounter.Waves)
                    yield return (wave.Id, encounter.Name + " / " + wave.Name);
                break;
            case MissionSignals.Timer:
                var timers = new HashSet<string>();
                foreach (var rule in mission.Events)
                foreach (var action in rule.Actions)
                    if (action.Type == MissionAction.Timer && timers.Add(action.TargetId))
                        yield return (action.TargetId, action.TargetId);
                break;
            case MissionSignals.Interaction:
                foreach (var door in layout.Doors)
                    yield return (door.Id, door.Name);
                foreach (var item in layout.Objects)
                    if (item.Container != null || SceneAssetRules.IsContainer(item))
                        yield return (item.Id, item.Name);
                break;
        }
    }

    public static IEnumerable<(string Id, string Name)> ActionTargets(MissionDefinition mission, MapLayout layout, string type)
    {
        if (type == MissionAction.Objective)
        {
            foreach (var objective in mission.Objectives)
                yield return (objective.Id, objective.Name);
        }
        else
        {
            foreach (var encounter in layout.Encounters)
                if (encounter.Trigger.Type == MapEncounterTrigger.Event)
                    yield return (encounter.Id, encounter.Name);
        }
    }

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
                .TargetIds.AsValueEnumerable()
                .Select(id =>
                    objective.TargetKind == "Squad" && id.Contains(':')
                        ? Map(id.Substring(0, id.IndexOf(':'))) + id.Substring(id.IndexOf(':'))
                        : Map(id)
                )
                .ToList();
        }
        foreach (var gate in mission.Requirements)
        {
            gate.CheckpointId = Map(gate.CheckpointId);
            gate.ObjectiveIds = gate.ObjectiveIds.AsValueEnumerable().Select(Map).ToList();
        }
    }
}
