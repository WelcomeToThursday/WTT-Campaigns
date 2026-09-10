namespace WTT.Campaigns.Shared.Story;

public sealed class StoryMedia
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "Image";
    public string Bundle { get; set; } = "";
    public string Asset { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string TraderId { get; set; } = "";

    public bool ShouldSerializeTraderId() => TraderId.Length > 0;
}
