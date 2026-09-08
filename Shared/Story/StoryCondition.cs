namespace SeasonalPerks.Shared.Story;

public sealed class StoryCondition
{
    public string Type { get; set; } = "All";
    public List<StoryCondition> Conditions { get; set; } = new();
    public string Target { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string Operator { get; set; } = ">=";
    public double Value { get; set; } = 1;
    public List<string> Status { get; set; } = new();
    public bool InRaidOnly { get; set; }
}
