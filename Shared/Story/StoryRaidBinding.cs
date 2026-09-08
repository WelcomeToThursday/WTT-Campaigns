namespace SeasonalPerks.Shared.Story;

public sealed class StoryRaidBinding
{
    public string Id { get; set; } = "";
    public string Location { get; set; } = "";
    public string Kind { get; set; } = "Trigger";
    public string ObjectPath { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string EntryPointId { get; set; } = "";
    public string MediaId { get; set; } = "";
    public bool Once { get; set; } = true;
    public bool PersistOnDeath { get; set; }
    public StoryCondition Condition { get; set; } = new();
    public List<StoryAction> Actions { get; set; } = new();
}
