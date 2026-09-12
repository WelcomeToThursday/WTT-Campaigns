using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Client.Profiles;

internal static class ProfileReconnect
{
    internal static async Task Run<TVisual>(Snapshot<TVisual> snapshot, Action<bool> showOverlay, Func<Task> reconnect)
        where TVisual : class
    {
        var needsCreation =
            snapshot.ActiveMode == "normal"
            && snapshot.Characters.Exists(c => c.Id == snapshot.EffectiveProfileId && c.Mode == "normal" && !c.Exists);
        if (!needsCreation)
        {
            await reconnect();
            return;
        }

        // PrepareGame waits for native profile creation after a launcher wipe.
        // Campaign overlays must release both visibility and input for that wait.
        showOverlay(false);
        try
        {
            await reconnect();
        }
        finally
        {
            showOverlay(true);
        }
    }
}
