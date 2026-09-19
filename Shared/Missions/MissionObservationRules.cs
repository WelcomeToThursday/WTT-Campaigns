using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>Server boundary for observations, separate from internally produced mission events.</summary>
public static class MissionObservationRules
{
    public static void Apply(MissionDefinition mission, MapLayout layout, MissionRun run, MissionRequest request, double maximumTime)
    {
        if (run.Restoring || run.TechnicalFailure)
            throw new InvalidOperationException("Mission observations are frozen during checkpoint restoration.");
        if (request.AttemptGeneration != run.AttemptGeneration)
            throw new InvalidOperationException("Mission observations belong to a retired attempt.");
        if (request.Signals == null || request.Signals.Count is < 1 or > 256)
            throw new InvalidOperationException("Report 1–256 mission observations per operation.");
        foreach (var signal in request.Signals)
        {
            if (signal == null || !double.IsFinite(signal.Time) || signal.Time > maximumTime + 2)
                throw new InvalidOperationException("Mission observation time is invalid.");
            var zone = layout.Checkpoints.AsValueEnumerable().Any(c => c.Id == signal.TargetId) || layout.Exit?.Id == signal.TargetId;
            var valid = signal.Kind switch
            {
                MissionSignals.Start => signal.TargetId.Length == 0 && !run.Logic.Started,
                MissionSignals.Spawn or MissionSignals.Death => run.Logic.Actors.ContainsKey(signal.ProfileId),
                MissionSignals.Enter or MissionSignals.Leave => zone,
                MissionSignals.Sample => zone
                    && signal.Occupants != null
                    && signal.Occupants.Count <= 4096
                    && signal.Occupants.AsValueEnumerable().Distinct().Count() == signal.Occupants.Count
                    && signal
                        .Occupants.AsValueEnumerable()
                        .All(id => run.Logic.Actors.TryGetValue(id, out var actor) && actor.Spawned && !actor.Dead),
                MissionSignals.Interaction => layout.Doors.AsValueEnumerable().Any(d => d.Id == signal.TargetId)
                    || layout
                        .Objects.AsValueEnumerable()
                        .Any(o => o.Id == signal.TargetId && (o.Container != null || SceneAssetRules.IsContainer(o))),
                MissionSignals.Tick => signal.TargetId.Length == 0,
                MissionSignals.Wave => layout
                    .Encounters.AsValueEnumerable()
                    .SelectMany(e => e.Waves)
                    .Any(w =>
                        w.Id == signal.TargetId && Complete(run, w.Roster.AsValueEnumerable().Sum(r => r.Count), a => a.WaveId == w.Id)
                    ),
                MissionSignals.Encounter => layout
                    .Encounters.AsValueEnumerable()
                    .Any(e =>
                        e.Id == signal.TargetId
                        && Complete(
                            run,
                            e.Waves.AsValueEnumerable().Sum(w => w.Roster.AsValueEnumerable().Sum(r => r.Count)),
                            a => a.EncounterId == e.Id
                        )
                    ),
                _ => false,
            };
            if (!valid)
                throw new InvalidOperationException("Unresolved or invalid mission observation: " + signal.Kind);
            MissionLogic.Apply(mission, layout, run.Logic, signal);
        }
    }

    private static bool Complete(MissionRun run, int expected, Func<MissionActor, bool> match)
    {
        var actors = run.Logic.Actors.Values.AsValueEnumerable().Where(match).ToArray();
        return expected > 0 && actors.Length == expected && actors.AsValueEnumerable().All(a => a.Spawned && a.Dead);
    }
}
