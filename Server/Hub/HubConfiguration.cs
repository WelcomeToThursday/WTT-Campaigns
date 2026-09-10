namespace WTT.Campaigns.Server.Hub;

public sealed class HubConfiguration
{
    public int DocumentsPerRaid { get; set; } = 8;
    public int ClassifiedChancePercent { get; set; } = 5;
    public Dictionary<string, int> MapCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
