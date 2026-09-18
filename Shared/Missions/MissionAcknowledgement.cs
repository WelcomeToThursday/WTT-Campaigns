namespace WTT.Campaigns.Shared.Missions;

/// <summary>Only a committed response for the exact attempt may release its queued observations.</summary>
public static class MissionAcknowledgement
{
    public static void RequireRestore(MissionRun expected, MissionRun? returned, bool committed, bool preparing)
    {
        if (returned == null) throw new InvalidOperationException("Missing checkpoint acknowledgement.");
        var generation = preparing ? checked(expected.AttemptGeneration + 1) : expected.AttemptGeneration;
        if (expected.Status != MissionRunStatuses.Active || returned.Status != MissionRunStatuses.Active
            || returned.AttemptGeneration != generation || returned.Restoring != preparing || expected.Restoring == preparing)
            throw new InvalidOperationException($"Checkpoint {(preparing ? "prepare" : "commit")} was not acknowledged: "
                + $"expected active attempt {generation}, restoring={preparing}; received {returned.Status} attempt {returned.AttemptGeneration}, restoring={returned.Restoring}. "
                + $"Previous state: {expected.Status}, restoring={expected.Restoring}.");
        var identity = new MissionRun
        {
            RunId = expected.RunId, RaidId = expected.RaidId, CharacterId = expected.CharacterId,
            MissionId = expected.MissionId, LayoutId = expected.LayoutId, ContentRevision = expected.ContentRevision,
            ContentHash = expected.ContentHash, AttemptGeneration = generation, Restoring = preparing,
            Status = expected.Status,
        };
        Require(identity, returned, committed);
    }
    public static void Require(MissionRun expected, MissionRun? returned, bool committed)
    {
        if (!committed || returned == null || returned.RunId != expected.RunId || returned.RaidId != expected.RaidId
            || returned.CharacterId != expected.CharacterId || returned.MissionId != expected.MissionId
            || returned.LayoutId != expected.LayoutId || returned.ContentRevision != expected.ContentRevision
            || returned.ContentHash != expected.ContentHash || returned.AttemptGeneration != expected.AttemptGeneration
            || returned.Restoring != expected.Restoring || returned.Status != expected.Status)
            throw new InvalidOperationException("The server did not acknowledge this mission attempt.");
    }
}
