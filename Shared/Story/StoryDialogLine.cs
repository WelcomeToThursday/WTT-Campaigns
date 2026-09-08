namespace SeasonalPerks.Shared.Story;

public sealed class StoryDialogLine
{
    public string Id { get; set; } = "";
    public string Side { get; set; } = "Player";
    public string Text { get; set; } = "";
    public string Icon { get; set; } = "DialogBubble";
    public string Confirmation { get; set; } = "";
    public string TraderId { get; set; } = "";
    public StoryCondition Trigger { get; set; } = new();
    public List<StoryAction> Actions { get; set; } = new();
    public StoryPlayback Playback { get; set; } = new();
    public StoryRandomGate? Random { get; set; }
}
