namespace SeasonalPerks.Shared.Story;

public sealed class StoryChapter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string Icon { get; set; } = "";
    public int Order { get; set; }
    public StoryCondition Visibility { get; set; } = new();
}
