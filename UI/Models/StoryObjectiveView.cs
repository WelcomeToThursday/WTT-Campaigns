namespace SeasonalPerks.UI.Models;

public sealed class StoryObjectiveView
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Counter { get; set; } = "";
    public bool Main { get; set; }
    public bool Complete { get; set; }
    public bool Failed { get; set; }
    public bool Unread { get; set; }
}
