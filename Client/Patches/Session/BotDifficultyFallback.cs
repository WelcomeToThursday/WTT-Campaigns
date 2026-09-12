namespace WTT.Campaigns.Client.Patches.Session;

internal static class BotDifficultyFallback
{
    internal static string Resolve<T>(string role, IReadOnlyDictionary<string, T> difficulties)
    {
        // SPT uses assault settings for roles absent from its database. Client-side
        // enum extensions can introduce additional roles after that response is built.
        return !difficulties.ContainsKey(role) && difficulties.ContainsKey("assault") ? "assault" : role;
    }
}
