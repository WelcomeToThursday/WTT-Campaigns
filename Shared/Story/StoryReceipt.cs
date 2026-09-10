namespace WTT.Campaigns.Shared.Story;

public sealed class StoryReceipt
{
    public string RequestHash { get; set; } = "";
    public long Revision { get; set; }
    public long Timestamp { get; set; }
    public List<StoryAction> Presentation { get; set; } = new();
    public string LineId { get; set; } = "";
    public string NativeUpdate { get; set; } = "";
}
