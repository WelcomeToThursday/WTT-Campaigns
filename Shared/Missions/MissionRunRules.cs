using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>
/// Pure route and extraction rules shared by the server and offline checks.
/// Mutating methods only change the supplied mission run after all validation
/// for the requested transition has passed.
/// </summary>
public static class MissionRunRules
{
    private static readonly HashSet<string> AliveExtractionResults = new(StringComparer.OrdinalIgnoreCase)
    {
        "survived",
        "runner",
        "runthrough",
        "transit",
    };

    public static bool MatchesIdentity(MissionRun run, string characterId, string runId, string raidId, string? encounterToken = null)
    {
        if (run == null || string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(raidId))
            return false;
        return string.Equals(run.CharacterId, characterId, StringComparison.Ordinal)
            && string.Equals(run.RunId, runId, StringComparison.Ordinal)
            && string.Equals(run.RaidId, raidId, StringComparison.Ordinal)
            && (encounterToken == null || string.Equals(run.EncounterToken, encounterToken, StringComparison.Ordinal));
    }

    public static bool TryCheckpoint(MissionRun run, IReadOnlyList<MapVolume> checkpoints, string checkpointId, out string error)
    {
        error = "";
        if (run == null)
        {
            error = "The mission run is unavailable.";
            return false;
        }
        if (run.Restoring || run.PlayerDefeated)
        {
            error = "Checkpoint restoration must finish before mission progress can continue.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(checkpointId))
        {
            error = "A checkpoint identity is required.";
            return false;
        }

        run.CompletedCheckpointIds ??= new();

        // Reports are retried by the client when a response is lost. A
        // duplicate authored checkpoint is therefore an accepted no-op.
        if (run.CompletedCheckpointIds.Contains(checkpointId))
            return true;
        if (run.NextCheckpointIndex < 0 || run.NextCheckpointIndex >= checkpoints.Count)
        {
            error = "All mission checkpoints are already complete.";
            return false;
        }

        var expected = checkpoints[run.NextCheckpointIndex];
        if (expected == null || !string.Equals(expected.Id, checkpointId, StringComparison.Ordinal))
        {
            error = "Mission checkpoints must be completed in authored order.";
            return false;
        }

        run.CompletedCheckpointIds.Add(expected.Id);
        run.NextCheckpointIndex++;
        run.CheckpointId = expected.Id;
        return true;
    }

    public static bool TryExit(MissionRun run, IReadOnlyList<MapVolume> checkpoints, MapVolume? exit, string exitId, out string error)
    {
        error = "";
        if (run == null || exit == null || string.IsNullOrWhiteSpace(exitId) || !string.Equals(exit.Id, exitId, StringComparison.Ordinal))
        {
            error = "This is not the authored mission exit.";
            return false;
        }
        if (run.NextCheckpointIndex != checkpoints.Count)
        {
            error = "Complete every mission checkpoint before extracting.";
            return false;
        }

        if (run.Restoring || run.PlayerDefeated)
        {
            error = "Checkpoint restoration must finish before extraction.";
            return false;
        }

        run.ExitReached = true;
        return true;
    }

    public static bool IsSuccessfulExtraction(
        MissionRun run,
        IReadOnlyList<MapVolume> checkpoints,
        MapVolume? exit,
        string? result,
        string? exitName,
        out string failureReason
    )
    {
        if (run == null)
        {
            failureReason = "The mission run is unavailable.";
            return false;
        }
        if (!AliveExtractionResults.Contains(result ?? ""))
        {
            failureReason = "The mission raid did not end with a successful extraction.";
            return false;
        }
        if (run.Restoring || run.PlayerDefeated)
        {
            failureReason = "Checkpoint restoration did not finish.";
            return false;
        }
        if (!run.ExitReached)
        {
            failureReason = "The authored mission exit was not reached.";
            return false;
        }
        if (run.NextCheckpointIndex != checkpoints.Count)
        {
            failureReason = "Every authored mission checkpoint must be completed.";
            return false;
        }
        if (exit == null || string.IsNullOrWhiteSpace(exit.Name) || !string.Equals(exitName, exit.Name, StringComparison.Ordinal))
        {
            failureReason = "The raid ended through an extraction other than the authored mission exit.";
            return false;
        }

        failureReason = "";
        return true;
    }
}
