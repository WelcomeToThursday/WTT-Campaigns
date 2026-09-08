namespace SeasonalPerks.Shared.Story;

public sealed class StoryObjective
{
    public string Id { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public string Text { get; set; } = "";
    public string Hint { get; set; } = "";
    public double Current { get; set; }
    public double Required { get; set; }
    public bool Main { get; set; }
    public bool Complete { get; set; }
    public bool Failed { get; set; }
    public bool Visible { get; set; }
}
