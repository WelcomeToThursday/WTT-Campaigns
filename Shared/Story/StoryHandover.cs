namespace WTT.Campaigns.Shared.Story;

public sealed class StoryHandover
{
    public string ActionId { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string ConditionId { get; set; } = "";
    public string ConditionJson { get; set; } = "";
    public double Current { get; set; }
    public List<string> Candidates { get; set; } = new();
}
