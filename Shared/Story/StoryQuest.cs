namespace SeasonalPerks.Shared.Story;

// Native quest definitions remain in SeasonDefinition.Quests; this adds story membership and lifecycle.
public sealed class StoryQuest
{
    public string QuestId { get; set; } = "";
    public string ChapterId { get; set; } = "";
    public bool Main { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool AutoComplete { get; set; }
    public bool Hidden { get; set; }
    public StoryCondition Visibility { get; set; } = new();
    public Dictionary<string, List<string>> StatusNotes { get; set; } = new();
}
