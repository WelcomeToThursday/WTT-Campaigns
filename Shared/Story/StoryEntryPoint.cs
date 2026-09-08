namespace SeasonalPerks.Shared.Story;

public sealed class StoryEntryPoint
{
    public string StartPoint { get; set; } = "";
    public string Id { get; set; } = "";
    public string TraderId { get; set; } = "";
    public string DialogId { get; set; } = "";
    public string Kind { get; set; } = "InLobby";
    public string Scene { get; set; } = "";
    public StoryCondition Condition { get; set; } = new();
}
