namespace WTT.Campaigns.Shared.Story;

public sealed class StoryConversation
{
    public string Id { get; set; } = "";
    public string EntryPointId { get; set; } = "";
    public string DialogId { get; set; } = "";
    public string TraderId { get; set; } = "";
    public string CurrentLineId { get; set; } = "";
    public string SelectedQuestId { get; set; } = "";
    public string SelectedServiceId { get; set; } = "";
    public Dictionary<string, int> Variables { get; set; } = new();
    public Dictionary<string, int> RandomValues { get; set; } = new();
    public List<string> History { get; set; } = new();
    public List<string> DialogStack { get; set; } = new();
    public bool Closed { get; set; }
}
