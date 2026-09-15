namespace WTT.Campaigns.Client.Authoring;

internal static class EditorStartupRecovery
{
    internal static bool Prepare(bool initialBackend, Func<bool> prepare, Action<Exception> recover)
    {
        try
        {
            return prepare();
        }
        catch (Exception error) when (initialBackend)
        {
            recover(error);
            return false;
        }
    }
}
