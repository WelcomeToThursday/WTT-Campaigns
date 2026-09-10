namespace WTT.Campaigns.Shared.Story;

// The runtime uses normalized lines and predicates, like the captured TraderDialogTemplate format.
public sealed class StoryDialog
{
    public string Id { get; set; } = "";
    public string TraderId { get; set; } = "";
    public string MainVariable { get; set; } = "";
    public Dictionary<string, int> StartPoints { get; set; } = new();
    public List<StoryDialogLine> Lines { get; set; } = new();
}
