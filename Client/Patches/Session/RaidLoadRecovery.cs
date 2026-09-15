namespace WTT.Campaigns.Client.Patches.Session;

internal static class RaidLoadRecovery
{
    internal static Func<Task> SelectCleanup(bool editorMapLoad, Func<Task> editorCleanup, Func<Task> gameplayCleanup) =>
        editorMapLoad ? editorCleanup : gameplayCleanup;

    internal static async Task Complete(Task loading, Func<Task> abort, Action<Exception> report)
    {
        try
        {
            await loading;
        }
        catch
        {
            try
            {
                await abort();
            }
            catch (Exception error)
            {
                report(error);
            }
            // Preserve the actual raid failure so EFT still performs its normal error/menu handling.
            throw;
        }
    }
}
