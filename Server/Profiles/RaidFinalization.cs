namespace WTT.Campaigns.Server.Profiles;

/// <summary>Retry post-native work without applying native inventory reconciliation twice.</summary>
internal static class RaidFinalization
{
    internal static bool RequiresNative(bool hubFinished, bool missionFinished) => !hubFinished && !missionFinished;

    internal static async Task Complete(Task native, IDisposable lease, Func<Task> hub, Func<Task> story,
        Func<Task> mission, Func<Task> clearRaid)
    {
        using (lease)
        {
            await native;
            await hub();
            await story();
            await mission();
        }
        // This must also run on a retry where all completion receipts already
        // exist: the previous request may have failed only while clearing it.
        await clearRaid();
    }
}
