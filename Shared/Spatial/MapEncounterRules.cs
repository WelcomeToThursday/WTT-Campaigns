using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Shared.Spatial;

/// <summary>
/// The small navigation seam needed by encounter validation. The client supplies a NavMesh-backed
/// implementation; keeping the seam here lets draft validation and tests remain independent of Unity.
/// </summary>
public interface IEncounterNavigation
{
    bool IsOnNavMesh(SpatialVector position);

    bool HasStandingClearance(SpatialVector position);

    bool HasCompletePath(SpatialVector from, SpatialVector to);
}

public static class MapEncounterRules
{
    public const int MaxSpawnPoints = 512;
    public const int MaxPatrolRoutes = 128;
    public const int MaxEncounters = 128;
    public const int MaxWaves = 64;
    public const int MaxRosterEntries = 128;
    public const int MaxBotsPerWave = 256;

    public static bool HasAi(MapLayout layout)
    {
        return layout != null && (layout.SpawnPoints?.Count > 0 || layout.Encounters?.Count > 0 || layout.PatrolRoutes?.Count > 0);
    }

    /// <summary>
    /// Returns structural errors for drafts. Pass a NavMesh adapter and requireNavigation=true before preview
    /// or activation; leaving the adapter out intentionally keeps imported invalid records editable.
    /// </summary>
    public static List<string> Errors(
        MapLayout layout,
        IEncounterNavigation? navigation = null,
        bool requireNavigation = false,
        bool requireComplete = false
    )
    {
        var errors = new List<string>();
        if (layout == null)
        {
            errors.Add("Map layout is required.");
            return errors;
        }

        if (layout.SpawnPoints == null || layout.Encounters == null || layout.PatrolRoutes == null)
        {
            errors.Add("AI collections cannot be null.");
            return errors;
        }

        Need(layout.SpawnPoints.Count <= MaxSpawnPoints, "At most " + MaxSpawnPoints + " AI spawn points are supported.");
        Need(layout.PatrolRoutes.Count <= MaxPatrolRoutes, "At most " + MaxPatrolRoutes + " patrol routes are supported.");
        Need(layout.Encounters.Count <= MaxEncounters, "At most " + MaxEncounters + " encounters are supported.");

        foreach (var spawn in layout.SpawnPoints)
        {
            if (spawn == null)
            {
                errors.Add("AI spawn points cannot contain null records.");
                continue;
            }

            ValidateNavigation(spawn, "Spawn point " + spawn.Id, navigation, requireNavigation, errors);
        }

        var routeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in layout.PatrolRoutes)
        {
            if (route == null)
            {
                errors.Add("Patrol routes cannot contain null records.");
                continue;
            }

            var path = "Patrol route " + route.Id;
            Need(routeIds.Add(route.Id), "Duplicate patrol route identity: " + route.Id);
            Need(SeasonValidator.IsId(route.Id), "Invalid patrol route identity: " + route.Id);
            Need(!string.IsNullOrWhiteSpace(route.Name) && route.Name.Length <= 120, "Patrol route name is required: " + route.Id);
            Need(
                route.Waypoints != null && (!requireComplete || route.Waypoints.Count >= 2),
                "Patrol routes require at least two waypoints: " + route.Id
            );
            Need(route.Pace is MapPatrolRoute.Walk or MapPatrolRoute.Run, "Unknown patrol pace: " + route.Id);
            Need(
                route.Completion is MapPatrolRoute.Loop or MapPatrolRoute.PingPong or MapPatrolRoute.Stop,
                "Unknown patrol completion mode: " + route.Id
            );
            if (route.Waypoints == null)
            {
                continue;
            }

            if (route.WaitSeconds == null || route.WaitSeconds.Count == 0)
            {
                // Empty is the compact representation for zero waits at every waypoint.
            }
            else
            {
                Need(
                    route.WaitSeconds.Count == route.Waypoints.Count,
                    "Patrol waits must contain one value per waypoint or be empty: " + route.Id
                );
                foreach (var wait in route.WaitSeconds)
                {
                    Need(float.IsFinite(wait) && wait >= 0 && wait <= 3600, "Invalid patrol wait: " + route.Id);
                }
            }

            var splineError = RouteSpline.Error(route.Spline, route.Waypoints, route.Completion == MapPatrolRoute.Loop);
            Need(splineError.Length == 0, path + ": " + splineError);
            if (route.Spline != null && navigation != null && splineError.Length == 0)
            {
                var walkability = navigation is IEncounterSplineNavigation curves
                    ? curves.SplineError(route)
                    : "Spline navigation validation is unavailable.";
                Need(walkability.Length == 0, path + ": " + walkability);
            }

            var waypoints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var waypoint in route.Waypoints)
            {
                if (waypoint == null)
                {
                    errors.Add("Patrol waypoints cannot contain null records: " + route.Id);
                    continue;
                }

                Need(waypoints.Add(waypoint.Id), "Duplicate patrol waypoint identity: " + waypoint.Id);
                ValidateNavigation(waypoint, path, navigation, requireNavigation, errors);
            }

            if (navigation != null && route.Spline == null && route.Waypoints.Count >= 2)
            {
                for (var i = 0; i < route.Waypoints.Count - 1; i++)
                {
                    var from = route.Waypoints[i];
                    var to = route.Waypoints[i + 1];
                    if (from != null && to != null)
                    {
                        Need(
                            navigation.HasCompletePath(from.Position, to.Position),
                            "Patrol route contains an unreachable segment: " + route.Id
                        );
                        if (route.Completion == MapPatrolRoute.PingPong)
                            Need(
                                navigation.HasCompletePath(to.Position, from.Position),
                                "Patrol route return segment is unreachable: " + route.Id
                            );
                    }
                }

                if (route.Completion == MapPatrolRoute.Loop)
                {
                    var from = route.Waypoints[^1];
                    var to = route.Waypoints[0];
                    if (from != null && to != null)
                    {
                        Need(
                            navigation.HasCompletePath(from.Position, to.Position),
                            "Patrol route loop closure is unreachable: " + route.Id
                        );
                    }
                }
            }
        }

        var spawnIds = layout.SpawnPoints.AsValueEnumerable().Where(p => p != null).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var encounterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var encounter in layout.Encounters)
        {
            if (encounter == null)
            {
                errors.Add("Encounters cannot contain null records.");
                continue;
            }

            var path = "Encounter " + encounter.Id;
            Need(encounterIds.Add(encounter.Id), "Duplicate encounter identity: " + encounter.Id);
            Need(SeasonValidator.IsId(encounter.Id), "Invalid encounter identity: " + encounter.Id);
            Need(!string.IsNullOrWhiteSpace(encounter.Name) && encounter.Name.Length <= 120, "Encounter name is required: " + encounter.Id);
            ValidateTrigger(layout, encounter.Trigger, path, errors, requireComplete);

            if (encounter.Waves == null || encounter.Waves.Count == 0)
            {
                if (requireComplete)
                    errors.Add("Encounters require at least one wave: " + encounter.Id);
                continue;
            }

            Need(encounter.Waves.Count <= MaxWaves, "Encounter has too many waves: " + encounter.Id);
            var waveIds = new HashSet<string>(StringComparer.Ordinal);
            var squadRoutes = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var waveIndex = 0; waveIndex < encounter.Waves.Count; waveIndex++)
            {
                var wave = encounter.Waves[waveIndex];
                if (wave == null)
                {
                    errors.Add("Encounter waves cannot contain null records: " + encounter.Id);
                    continue;
                }

                var wavePath = path + "/wave " + waveIndex;
                Need(waveIds.Add(wave.Id), "Duplicate encounter wave identity: " + wave.Id);
                Need(SeasonValidator.IsId(wave.Id), "Invalid encounter wave identity: " + wave.Id);
                Need(!string.IsNullOrWhiteSpace(wave.Name) && wave.Name.Length <= 120, "Wave name is required: " + wave.Id);
                Need(
                    float.IsFinite(wave.DelaySeconds) && wave.DelaySeconds >= 0 && wave.DelaySeconds <= 3600,
                    "Wave delay must be between zero and 3600 seconds: " + wave.Id
                );
                Need(!wave.WaitForPreviousWave || waveIndex > 0, "The first wave cannot wait for a previous wave: " + wave.Id);
                Need(
                    wave.Roster != null && (!requireComplete || wave.Roster.Count > 0),
                    "Each encounter wave requires a roster: " + wave.Id
                );
                if (wave.Roster == null)
                {
                    continue;
                }

                Need(wave.Roster.Count <= MaxRosterEntries, "Wave has too many roster entries: " + wave.Id);
                var rosterIds = new HashSet<string>(StringComparer.Ordinal);
                var reservedSpawnIds = new HashSet<string>(StringComparer.Ordinal);
                var total = 0;
                foreach (var roster in wave.Roster)
                {
                    if (roster == null)
                    {
                        errors.Add("Encounter rosters cannot contain null records: " + wave.Id);
                        continue;
                    }

                    var rosterPath = wavePath + "/roster " + roster.Id;
                    Need(rosterIds.Add(roster.Id), "Duplicate encounter roster identity: " + roster.Id);
                    Need(SeasonValidator.IsId(roster.Id), "Invalid encounter roster identity: " + roster.Id);
                    Need(!string.IsNullOrWhiteSpace(roster.Role) && roster.Role.Length <= 120, "Bot role is required: " + roster.Id);
                    if (requireComplete)
                    {
                        Need(
                            roster.Role is "assault" or "pmcUSEC" or "pmcBEAR",
                            "This bot role has no verified generation, SAIN and patrol integration: " + roster.Role
                        );
                        Need(
                            roster.Difficulty is "easy" or "normal" or "hard" or "impossible",
                            "Unsupported bot difficulty: " + roster.Difficulty
                        );
                    }
                    Need(
                        !string.IsNullOrWhiteSpace(roster.Difficulty) && roster.Difficulty.Length <= 120,
                        "Bot difficulty is required: " + roster.Id
                    );
                    Need(roster.Count is >= 1 and <= MaxBotsPerWave, "Bot count is outside the supported range: " + roster.Id);
                    total += Math.Max(roster.Count, 0);
                    if (total > MaxBotsPerWave)
                    {
                        errors.Add("A wave cannot activate more than " + MaxBotsPerWave + " bots: " + wave.Id);
                        total = MaxBotsPerWave;
                    }

                    if (roster.SquadId.Length > 0)
                    {
                        Need(
                            roster.SquadId.Length <= 80 && !string.IsNullOrWhiteSpace(roster.SquadId),
                            "Squad name must be at most 80 characters: " + roster.Id
                        );
                        if (squadRoutes.TryGetValue(roster.SquadId, out var existingRoute))
                        {
                            Need(
                                existingRoute == roster.PatrolRouteId,
                                "Roster entries in one squad must use one patrol route: " + roster.SquadId
                            );
                        }
                        else
                        {
                            squadRoutes[roster.SquadId] = roster.PatrolRouteId;
                        }
                    }

                    Need(roster.SpawnPointIds != null, "Spawn assignments cannot be null: " + roster.Id);
                    if (roster.SpawnPointIds == null)
                    {
                        continue;
                    }

                    Need(
                        !requireComplete || roster.SpawnPointIds.Count >= roster.Count,
                        "A roster requires at least one authored spawn point per bot: " + roster.Id
                    );
                    var entrySpawnIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var spawnId in roster.SpawnPointIds)
                    {
                        Need(entrySpawnIds.Add(spawnId), "A roster cannot reuse one spawn point: " + roster.Id);
                        Need(spawnIds.Contains(spawnId), "Roster references an unknown spawn point: " + spawnId);
                        Need(reservedSpawnIds.Add(spawnId), "Concurrent roster entries cannot share a spawn point: " + spawnId);
                    }

                    if (roster.PatrolRouteId.Length > 0)
                    {
                        Need(SeasonValidator.IsId(roster.PatrolRouteId), "Invalid patrol route reference: " + roster.Id);
                        Need(routeIds.Contains(roster.PatrolRouteId), "Roster references an unknown patrol route: " + roster.Id);
                    }
                }
            }
        }

        if (requireNavigation && HasAi(layout) && navigation == null)
        {
            errors.Add("NavMesh validation is required before an AI preview can run.");
        }

        return errors;

        void Need(bool valid, string error)
        {
            if (!valid)
            {
                errors.Add(error);
            }
        }
    }

    private static void ValidateTrigger(
        MapLayout layout,
        MapEncounterTrigger? trigger,
        string path,
        List<string> errors,
        bool requireComplete
    )
    {
        if (trigger == null)
        {
            errors.Add("Encounter trigger is required: " + path);
            return;
        }

        switch (trigger.Type)
        {
            case MapEncounterTrigger.PlayerEntry:
                if (requireComplete && trigger.Volume == null && string.IsNullOrWhiteSpace(trigger.ZoneId))
                    errors.Add("Player-entry encounters require a trigger volume or zone reference: " + path);
                if (trigger.Volume != null)
                {
                    if (!SeasonValidator.IsId(trigger.Volume.Id))
                        errors.Add("Encounter trigger volume identity is invalid: " + path);
                    if (trigger.Volume.Location != layout.Location)
                        errors.Add("Encounter trigger volume belongs to another map: " + path);
                }
                if (trigger.Volume != null && trigger.ZoneId.Length > 0)
                    errors.Add("Choose either an encounter volume or a checkpoint trigger reference: " + path);
                if (
                    trigger.ZoneId.Length > 0
                    && !layout.Checkpoints.AsValueEnumerable().Any(p => p.Id == trigger.ZoneId)
                    && layout.Exit?.Id != trigger.ZoneId
                )
                    errors.Add("Encounter trigger reference must identify a checkpoint or exit in this layout: " + path);
                if (trigger.EventId.Length > 0)
                    errors.Add("Player-entry encounters cannot specify an event: " + path);
                break;
            case MapEncounterTrigger.MissionStart:
                if (trigger.Volume != null || trigger.ZoneId.Length > 0 || trigger.EventId.Length > 0)
                    errors.Add("Mission-start encounters do not use a volume, zone or event: " + path);
                break;
            case MapEncounterTrigger.Event:
                if (requireComplete && (string.IsNullOrWhiteSpace(trigger.EventId) || trigger.EventId.Length > 120))
                    errors.Add("Event encounters require an event name up to 120 characters: " + path);
                if (trigger.Volume != null || trigger.ZoneId.Length > 0)
                    errors.Add("Event encounters do not use a volume or zone: " + path);
                break;
            default:
                errors.Add("Unknown encounter trigger type: " + path);
                break;
        }
    }

    private static void ValidateNavigation(
        SpatialCapture point,
        string path,
        IEncounterNavigation? navigation,
        bool requireNavigation,
        List<string> errors
    )
    {
        if (point.Position?.Finite != true)
        {
            errors.Add("Invalid AI transform: " + path);
        }

        if (!requireNavigation && navigation == null)
        {
            return;
        }

        if (navigation == null)
        {
            errors.Add("NavMesh validation is unavailable: " + path);
            return;
        }

        var position = point.Position!;
        if (!navigation.IsOnNavMesh(position))
            errors.Add("AI point is off the NavMesh: " + path);
        if (!navigation.HasStandingClearance(position))
            errors.Add("AI point lacks standing clearance: " + path);
    }
}
