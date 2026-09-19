using Newtonsoft.Json;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>A raid-local immutable rollback point. Native state is owned by the client and server adapters.</summary>
public sealed class MissionCheckpoint
{
    private readonly string _snapshot;
    public string Id { get; }
    public string RunId { get; }
    public string RaidId { get; }
    public double Time { get; }

    public MissionCheckpoint(MissionRun run, string checkpointId)
    {
        if (
            run.Status != MissionRunStatuses.Active
            || run.Restoring
            || run.PlayerDefeated
            || run.ExitReached
            || run.Logic.Failure.Length > 0
        )
            throw new InvalidOperationException("Only an active mission can accept a checkpoint.");
        if (checkpointId != run.CheckpointId || (checkpointId.Length > 0 && !run.CompletedCheckpointIds.Contains(checkpointId)))
            throw new InvalidOperationException("The checkpoint has not been accepted.");
        Id = checkpointId;
        RunId = run.RunId;
        RaidId = run.RaidId;
        Time = run.Logic.Time;
        _snapshot = JsonConvert.SerializeObject(run);
    }

    /// <summary>Invalidate the old attempt before native cleanup. No progress or extraction is legal until commit.</summary>
    public MissionRun BeginRestore(MissionRun current)
    {
        RequireIdentity(current);
        if (current.Status != MissionRunStatuses.Active || current.Restoring || current.ExitReached)
            throw new InvalidOperationException("This mission cannot begin a checkpoint retry.");
        var restored = JsonConvert.DeserializeObject<MissionRun>(_snapshot)!;
        restored.AttemptGeneration = checked(current.AttemptGeneration + 1);
        restored.Restoring = true;
        restored.CheckpointId = Id;
        restored.EncounterToken = Guid.NewGuid().ToString("N");
        // Generated profiles are attempt-owned. The adapter must register replacements
        // before commit; keeping old IDs here would authorize abandoned callbacks.
        restored.EncounterProfileChunks.Clear();
        return restored;
    }

    public void CommitRestore(
        MissionDefinition mission,
        MapLayout layout,
        MissionRun restored,
        IReadOnlyDictionary<string, string> replacementIds
    )
    {
        RequireIdentity(restored);
        if (!restored.Restoring || restored.CheckpointId != Id)
            throw new InvalidOperationException("No matching checkpoint restoration is pending.");
        var candidate = JsonConvert.DeserializeObject<MissionLogicState>(JsonConvert.SerializeObject(restored.Logic))!;
        var actors = candidate.Actors;
        var living = actors.Values.AsValueEnumerable().Where(a => a.Spawned && !a.Dead).ToArray();
        if (
            replacementIds.Count != living.Length
            || replacementIds.Values.AsValueEnumerable().Distinct(StringComparer.Ordinal).Count() != living.Length
        )
            throw new InvalidOperationException("Every surviving checkpoint actor requires a distinct replacement.");
        // Validate the whole mapping before changing any state.
        foreach (var actor in living)
        {
            if (
                !replacementIds.TryGetValue(actor.ProfileId, out var replacement)
                || string.IsNullOrWhiteSpace(replacement)
                || actors.ContainsKey(replacement)
            )
                throw new InvalidOperationException("A checkpoint actor replacement is missing or reuses an old runtime identity.");
        }
        foreach (var actor in living)
        {
            actors.Remove(actor.ProfileId);
            actor.ProfileId = replacementIds[actor.ProfileId];
            actors.Add(actor.ProfileId, actor);
        }
        // Unspawned generated profiles from the checkpoint are re-requested by the
        // restored wave and must never count as living or defeated actors.
        foreach (var actor in actors.Values.AsValueEnumerable().Where(a => !a.Spawned).ToArray())
            actors.Remove(actor.ProfileId);
        foreach (var zone in candidate.Zones.Values)
            zone.Occupants = zone.Occupants.AsValueEnumerable().Where(replacementIds.ContainsKey).Select(id => replacementIds[id]).ToList();
        MissionLogic.Apply(
            mission,
            layout,
            candidate,
            new MissionSignal
            {
                Kind = Id.Length == 0 ? MissionSignals.Start : MissionSignals.Checkpoint,
                TargetId = Id,
                Time = Time,
            }
        );
        restored.Logic = candidate;
        restored.Restoring = false;
    }

    private void RequireIdentity(MissionRun run)
    {
        if (run.RunId != RunId || run.RaidId != RaidId)
            throw new InvalidOperationException("The checkpoint belongs to another mission raid.");
    }
}
