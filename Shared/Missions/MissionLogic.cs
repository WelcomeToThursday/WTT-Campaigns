using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Missions;

public static class MissionSignals
{
    public const string Start = "MissionStart",
        Checkpoint = "CheckpointReached",
        Enter = "ZoneEntered",
        Leave = "ZoneExited",
        Interaction = "Interaction",
        Timer = "TimerElapsed",
        Spawn = "BotSpawned",
        Death = "BotDied",
        Wave = "WaveCompleted",
        Encounter = "EncounterCompleted",
        Complete = "ObjectiveCompleted",
        Fail = "ObjectiveFailed",
        Sample = "ZoneSample",
        Tick = "Tick",
        Exit = "MissionExit";
}

public sealed class MissionEventRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New event";
    public string Source { get; set; } = MissionSignals.Checkpoint;
    public string SourceId { get; set; } = "";
    public List<MissionAction> Actions { get; set; } = new();
}

public sealed class MissionAction
{
    public const string Encounter = "ActivateEncounter",
        Objective = "ActivateObjective",
        Timer = "StartTimer";
    public string Type { get; set; } = Encounter;
    public string TargetId { get; set; } = "";
    public double Seconds { get; set; }
}

public sealed class MissionObjective
{
    public const string Eliminate = "EliminateGroup",
        Target = "EliminateTarget",
        Survive = "SurviveWaves",
        Defend = "DefendArea",
        Protect = "ProtectActor";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New objective";
    public string Type { get; set; } = Eliminate;
    public bool OnStart { get; set; } = true;
    public bool Required { get; set; } = true;
    public string TargetKind { get; set; } = "Encounter";
    public List<string> TargetIds { get; set; } = new();
    public string ZoneId { get; set; } = "";
    public double Seconds { get; set; } = 60;

    // Empty protects through successful extraction; otherwise a mission event rule identity.
    public string UntilEventId { get; set; } = "";
}

public sealed class MissionRequirement
{
    // Empty means the authored exit. Otherwise this is an authored checkpoint identity.
    public string CheckpointId { get; set; } = "";
    public List<string> ObjectiveIds { get; set; } = new();
}

public sealed class MissionSignal
{
    public string Kind { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public double Time { get; set; }
    public bool PlayerInside { get; set; }
    public List<string> Occupants { get; set; } = new();
}

public sealed class MissionActor
{
    public string ProfileId { get; set; } = "";
    public string EncounterId { get; set; } = "";
    public string WaveId { get; set; } = "";
    public string RosterId { get; set; } = "";
    public string SquadId { get; set; } = "";
    public bool Spawned { get; set; }
    public bool Dead { get; set; }
}

public sealed class MissionObjectiveProgress
{
    public string Status { get; set; } = "Pending";
    public double Seconds { get; set; }
    public int Count { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>Serializable evaluator state. Native observations are validated before entering this state.</summary>
public sealed class MissionLogicState
{
    public double Time { get; set; }
    public bool Started { get; set; }
    public Dictionary<string, MissionObjectiveProgress> Objectives { get; set; } = new();
    public Dictionary<string, MissionActor> Actors { get; set; } = new();
    public HashSet<string> FiredRules { get; set; } = new();
    public HashSet<string> CompletedWaves { get; set; } = new();
    public HashSet<string> CompletedEncounters { get; set; } = new();
    public HashSet<string> ActivatedEncounters { get; set; } = new();
    public Dictionary<string, double> Timers { get; set; } = new();
    public HashSet<string> FinishedTimers { get; set; } = new();
    public Dictionary<string, MissionSignal> Zones { get; set; } = new();
    public string Failure { get; set; } = "";
}

/// <summary>One deterministic evaluator shared by live missions, rehearsals and offline checks.</summary>
public static class MissionLogic
{
    public static bool HasLogic(MissionDefinition definition) =>
        definition.CheckpointRetries || definition.Events.Count > 0 || definition.Objectives.Count > 0 || definition.Requirements.Count > 0;

    public static void Apply(MissionDefinition mission, MapLayout layout, MissionLogicState state, MissionSignal input)
    {
        if (!double.IsFinite(input.Time) || input.Time < state.Time)
            throw new InvalidOperationException("Mission observation time cannot move backwards.");
        var elapsed = input.Time - state.Time;
        // Accumulate using the previous occupancy sample, never retroactively credit a newly entered area.
        foreach (var objective in mission.Objectives.AsValueEnumerable().Where(o => o.Type == MissionObjective.Defend))
        {
            var progress = Progress(state, objective.Id);
            if (progress.Status != "Active")
                continue;
            var held =
                state.Zones.TryGetValue(objective.ZoneId, out var zone)
                && zone.PlayerInside
                && !zone
                    .Occupants.AsValueEnumerable()
                    .Any(id => state.Actors.TryGetValue(id, out var actor) && !actor.Dead && Matches(objective, actor));
            if (held)
                progress.Seconds = Math.Min(objective.Seconds, progress.Seconds + elapsed);
            progress.Detail = held ? "Holding" : "Paused · leave the area uncontested and stay inside";
        }
        state.Time = input.Time;
        var queue = new Queue<MissionSignal>();
        queue.Enqueue(input);
        foreach (var timer in state.Timers.AsValueEnumerable().Where(t => t.Value <= state.Time).ToArray())
        {
            state.Timers.Remove(timer.Key);
            state.FinishedTimers.Add(timer.Key);
            queue.Enqueue(
                new MissionSignal
                {
                    Kind = MissionSignals.Timer,
                    TargetId = timer.Key,
                    Time = state.Time,
                }
            );
        }
        var operations = 0;
        while (queue.Count > 0)
        {
            if (++operations > 2048)
                throw new InvalidOperationException("Mission event cascade exceeded its bounded queue.");
            var signal = queue.Dequeue();
            if (signal.Kind == MissionSignals.Start && !state.Started)
            {
                state.Started = true;
                foreach (var objective in mission.Objectives.AsValueEnumerable().Where(o => o.OnStart))
                    Activate(state, objective.Id);
            }
            if (signal.Kind is MissionSignals.Spawn or MissionSignals.Death)
            {
                if (!state.Actors.TryGetValue(signal.ProfileId, out var actor))
                    throw new InvalidOperationException("Mission observation references an unregistered actor.");
                if (signal.Kind == MissionSignals.Spawn)
                    actor.Spawned = true;
                else
                {
                    if (!actor.Spawned)
                        throw new InvalidOperationException("An unspawned actor cannot be defeated.");
                    actor.Dead = true;
                }
                signal.TargetId = actor.RosterId;
            }
            if (signal.Kind == MissionSignals.Sample)
                state.Zones[signal.TargetId] = signal;
            if (signal.Kind == MissionSignals.Wave)
                state.CompletedWaves.Add(signal.TargetId);
            if (signal.Kind == MissionSignals.Encounter)
                state.CompletedEncounters.Add(signal.TargetId);
            foreach (var rule in mission.Events.AsValueEnumerable().Where(r => r.Source == signal.Kind && r.SourceId == signal.TargetId))
            {
                if (!state.FiredRules.Add(rule.Id))
                    continue;
                foreach (var action in rule.Actions)
                {
                    if (action.Type == MissionAction.Encounter)
                        state.ActivatedEncounters.Add(action.TargetId);
                    else if (action.Type == MissionAction.Objective)
                        Activate(state, action.TargetId);
                    else if (
                        action.Type == MissionAction.Timer
                        && !state.FinishedTimers.Contains(action.TargetId)
                        && !state.Timers.ContainsKey(action.TargetId)
                    )
                        state.Timers.Add(action.TargetId, state.Time + action.Seconds);
                }
            }
            foreach (var objective in mission.Objectives)
            {
                var progress = Progress(state, objective.Id);
                if (progress.Status != "Active")
                    continue;
                var actors = state.Actors.Values.AsValueEnumerable().Where(a => Matches(objective, a)).ToArray();
                progress.Count = actors.AsValueEnumerable().Count(a => a.Dead);
                var expected = Expected(layout, objective);
                var complete = false;
                if (objective.Type is MissionObjective.Eliminate or MissionObjective.Target)
                    complete = expected > 0 && actors.Length == expected && actors.AsValueEnumerable().All(a => a.Spawned && a.Dead);
                else if (objective.Type == MissionObjective.Survive)
                    complete =
                        objective.TargetIds.Count > 0 && objective.TargetIds.AsValueEnumerable().All(state.CompletedEncounters.Contains);
                else if (objective.Type == MissionObjective.Defend)
                    complete = progress.Seconds >= objective.Seconds;
                else if (objective.Type == MissionObjective.Protect)
                {
                    if (actors.AsValueEnumerable().Any(a => a.Dead))
                    {
                        progress.Status = "Failed";
                        progress.Detail = "Protected actor died";
                        if (objective.Required)
                            state.Failure = objective.Name + ": protected actor died";
                        queue.Enqueue(new MissionSignal { Kind = MissionSignals.Fail, TargetId = objective.Id });
                        continue;
                    }
                    complete =
                        actors.Length == 1
                        && actors[0].Spawned
                        && (
                            objective.UntilEventId.Length == 0
                                ? signal.Kind == MissionSignals.Exit
                                : state.FiredRules.Contains(objective.UntilEventId)
                        );
                }
                if (!complete)
                    continue;
                progress.Status = "Completed";
                progress.Detail = "Completed";
                queue.Enqueue(new MissionSignal { Kind = MissionSignals.Complete, TargetId = objective.Id });
            }
        }
    }

    public static MissionObjectiveProgress Progress(MissionLogicState state, string id)
    {
        if (!state.Objectives.TryGetValue(id, out var progress))
            state.Objectives.Add(id, progress = new());
        return progress;
    }

    private static void Activate(MissionLogicState state, string id)
    {
        var progress = Progress(state, id);
        if (progress.Status == "Pending")
            progress.Status = "Active";
    }

    public static bool Matches(MissionObjective objective, MissionActor actor) =>
        objective.TargetIds.Contains(
            objective.TargetKind switch
            {
                "Roster" => actor.RosterId,
                "Squad" => actor.EncounterId + ":" + actor.SquadId,
                _ => actor.EncounterId,
            }
        );

    public static int Expected(MapLayout layout, MissionObjective objective) =>
        layout
            .Encounters.AsValueEnumerable()
            .Sum(e =>
                e.Waves.AsValueEnumerable()
                    .Sum(w =>
                        w.Roster.AsValueEnumerable()
                            .Where(r =>
                                Matches(
                                    objective,
                                    new MissionActor
                                    {
                                        EncounterId = e.Id,
                                        RosterId = r.Id,
                                        SquadId = r.SquadId,
                                    }
                                )
                            )
                            .Sum(r => r.Count)
                    )
            );

    public static bool CanAdvance(MissionDefinition mission, MissionLogicState state, string checkpointId, out string error)
    {
        error = state.Failure;
        if (error.Length > 0)
            return false;
        var ids = mission
            .Requirements.AsValueEnumerable()
            .Where(r => r.CheckpointId == checkpointId)
            .SelectMany(r => r.ObjectiveIds)
            .ToHashSet();
        if (checkpointId.Length == 0)
            ids.UnionWith(mission.Objectives.AsValueEnumerable().Where(o => o.Required).Select(o => o.Id).ToArray());
        foreach (var objective in mission.Objectives.AsValueEnumerable().Where(o => o.Required && ids.Contains(o.Id)))
        {
            var progress = Progress(state, objective.Id);
            if (progress.Status == "Completed")
                continue;
            // Protect-until-exit is checked for a living, spawned actor before admitting exit, then completed by MissionExit.
            if (
                checkpointId.Length == 0
                && objective.Type == MissionObjective.Protect
                && objective.UntilEventId.Length == 0
                && progress.Status == "Active"
            )
            {
                var actors = state.Actors.Values.AsValueEnumerable().Where(a => Matches(objective, a)).ToArray();
                if (actors.Length == 1 && actors[0].Spawned && !actors[0].Dead)
                    continue;
            }
            error = "Complete objective: " + objective.Name;
            return false;
        }
        return true;
    }
}
