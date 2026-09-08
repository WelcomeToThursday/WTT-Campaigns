namespace SeasonalPerks.Shared.Story;

public class StoryRequest
{
    public int Version { get; set; } = 1;
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
}
