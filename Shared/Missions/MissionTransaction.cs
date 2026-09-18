namespace WTT.Campaigns.Shared.Missions;

/// <summary>
/// Small, deterministic mission state transitions used by the server service.
/// Keeping validation and receipt eviction here makes the profile transaction
/// rules testable without booting SPT or constructing its DI graph.
/// </summary>
public static class MissionTransaction
{
    public const int DefaultMaxReceipts = 128;

    public static void RequireOperation(MissionRequest request)
    {
        if (!Guid.TryParseExact(request.OperationId, "N", out _))
            throw new InvalidOperationException("A unique mission operation identifier is required.");
    }

    public static void RequireRevision(MissionProgress state, long expected)
    {
        if (state.Revision != expected)
            throw new InvalidOperationException("Mission progress changed. Refresh and try again.");
    }

    /// <summary>Validates the request identity at the active-session boundary.</summary>
    public static void RequireRequestIdentity(MissionRequest request, string actualCharacterId, string seasonId)
    {
        if (request.Version is not (1 or 2) || request.CharacterId != actualCharacterId || string.IsNullOrWhiteSpace(request.SeasonId))
            throw new InvalidOperationException("Refresh the active Campaign character before using missions.");
        if (request.SeasonId != seasonId)
            throw new InvalidOperationException("This mission request belongs to another campaign.");
    }

    public static void VerifyContent(MissionRun run, long revision, string hash)
    {
        if (run.ContentRevision != revision || run.ContentHash != hash)
            throw new InvalidOperationException("The mission content changed. Cancel the old run and prepare it again.");
    }

    /// <summary>
    /// Finds a receipt for an operation after checking that it belongs to the
    /// current run. The service uses the returned receipt to assemble a replay
    /// response; this helper deliberately does not build that response.
    /// </summary>
    public static bool TryReplayReceipt(MissionProgress state, string operationId, string fingerprint, out MissionReceipt? receipt)
    {
        receipt = null;
        if (string.IsNullOrWhiteSpace(operationId) || !state.Receipts.TryGetValue(operationId, out var existing))
            return false;
        if (existing.RequestHash != fingerprint)
            throw new InvalidOperationException("Mission operation identifier was reused for different inputs.");
        if (state.ActiveRun == null || string.IsNullOrWhiteSpace(existing.RunId) || state.ActiveRun.RunId != existing.RunId)
            throw new InvalidOperationException("This mission operation belongs to an older run. Refresh the mission list.");
        if (existing.AttemptGeneration != state.ActiveRun.AttemptGeneration)
            throw new InvalidOperationException("This mission operation belongs to a retired checkpoint attempt.");
        receipt = existing;
        return true;
    }

    public static bool Unlock(MissionProgress state, string missionId, string? questStatus)
    {
        if (state.UnlockedMissionIds.Contains(missionId))
            return false;
        if (questStatus is "Started" or "AvailableForFinish" or "Success")
        {
            state.UnlockedMissionIds.Add(missionId);
            return true;
        }
        throw new InvalidOperationException("Accept the linked quest before deploying this mission.");
    }

    public static void AddReceipt(
        MissionProgress state,
        string operationId,
        string fingerprint,
        string operation,
        MissionRun run,
        long timestamp,
        int maxReceipts = DefaultMaxReceipts
    )
    {
        if (maxReceipts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxReceipts));

        state.Receipts[operationId] = new MissionReceipt
        {
            RequestHash = fingerprint,
            Revision = state.Revision,
            Timestamp = timestamp,
            Operation = operation,
            RunId = run.RunId,
            AttemptGeneration = run.AttemptGeneration,
            Status = run.Status,
        };

        if (state.Receipts.Count <= maxReceipts)
            return;

        // Keep the just-committed receipt. Prefer evicting old terminal-run
        // receipts, then use timestamp and operation id for deterministic ties.
        var removable = state
            .Receipts.Where(entry => entry.Key != operationId)
            .OrderBy(entry => MissionRunStatuses.IsTerminal(entry.Value.Status) ? 0 : 1)
            .ThenBy(entry => entry.Value.Timestamp)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Key)
            .Take(state.Receipts.Count - maxReceipts)
            .ToArray();
        foreach (var key in removable)
            state.Receipts.Remove(key);
    }
}
