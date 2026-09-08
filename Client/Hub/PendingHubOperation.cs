namespace SeasonalPerks.Client.Hub;

internal sealed class PendingHubOperation
{
    public string Body { get; set; } = "";
    public string Action { get; set; } = "";
}
