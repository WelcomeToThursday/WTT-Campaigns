namespace WTT.Campaigns.Shared.Story;

public sealed class StoryRaid
{
    public string Id { get; set; } = "";
    public string Location { get; set; } = "";
    public bool Finished { get; set; }
    public HashSet<string> Seen { get; set; } = new();
    public HashSet<string> Pending { get; set; } = new();
    public string Cinematic { get; set; } = "";
    public long Experience { get; set; }
    public Dictionary<string, double> Skills { get; set; } = new();
    public Dictionary<string, double> Standing { get; set; } = new();
    public Dictionary<string, string> QuestTransitions { get; set; } = new();
    public Dictionary<string, string> SpawnedItems { get; set; } = new();
    public HashSet<string> PickedItems { get; set; } = new();
}
