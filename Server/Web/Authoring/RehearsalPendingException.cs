namespace SeasonalPerks.Server.Web.Authoring;

public sealed class RehearsalPendingException(string key, string message) : Exception(message)
{
    public string Key { get; } = key;
}
