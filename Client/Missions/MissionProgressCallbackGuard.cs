namespace WTT.Campaigns.Client.Missions;

/// <summary>
/// Pure identity gate used before applying an asynchronous mission progress response.
/// It keeps a delayed callback from a finished raid from changing a replacement run.
/// </summary>
internal static class MissionProgressCallbackGuard
{
    internal static bool IsCurrent(
        bool active,
        bool ending,
        bool lifetimeCancelled,
        bool sameLifetime,
        bool inRaid,
        bool samePlayer,
        bool sameWorld,
        bool sameDescriptor,
        int currentGeneration,
        int capturedGeneration,
        string? currentRunId,
        string capturedRunId,
        string? currentRaidId,
        string capturedRaidId,
        string? currentMissionId,
        string capturedMissionId
    )
    {
        return active
            && !ending
            && !lifetimeCancelled
            && sameLifetime
            && inRaid
            && samePlayer
            && sameWorld
            && sameDescriptor
            && currentGeneration == capturedGeneration
            && string.Equals(currentRunId, capturedRunId, StringComparison.Ordinal)
            && string.Equals(currentRaidId, capturedRaidId, StringComparison.Ordinal)
            && string.Equals(currentMissionId, capturedMissionId, StringComparison.Ordinal);
    }
}
