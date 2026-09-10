namespace WTT.Campaigns.Shared.Story;

public sealed class StoryRaidBinding
{
    public bool ShouldSerializeName()
    {
        return Name.Length > 0;
    }

    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Location { get; set; } = "";
    public string Kind { get; set; } = "Trigger";

    public bool ShouldSerializeZoneId()
    {
        return ZoneId.Length > 0;
    }

    public string ZoneId { get; set; } = "";
    public string ObjectPath { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string EntryPointId { get; set; } = "";
    public string MediaId { get; set; } = "";
    public bool Once { get; set; } = true;
    public bool PersistOnDeath { get; set; }
    public StoryCondition Condition { get; set; } = new();
    public List<StoryAction> Actions { get; set; } = new();
}
