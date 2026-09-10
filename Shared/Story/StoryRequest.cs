namespace WTT.Campaigns.Shared.Story;

public class StoryRequest
{
    public int Version { get; set; } = 2;
    public string SeasonId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public long ExpectedRevision { get; set; }
    public string ConversationId { get; set; } = "";
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "";
    public string RaidId { get; set; } = "";
    public List<string> ItemIds { get; set; } = new();
    public string ItemId { get; set; } = "";
    public string Scene { get; set; } = "";
    public string Operation { get; set; } = "";
    public string PreparationId { get; set; } = "";
    public Dictionary<string, List<string>> Selections { get; set; } = new();
    public StoryRaidObservation? Observation { get; set; }
}
