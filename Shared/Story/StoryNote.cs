namespace WTT.Campaigns.Shared.Story;

public sealed class StoryNote
{
    public string Id { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> ConditionIds { get; set; } = new();
    public List<StoryNoteLink> Links { get; set; } = new();
}
