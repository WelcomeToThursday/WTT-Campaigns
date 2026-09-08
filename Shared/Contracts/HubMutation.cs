namespace SeasonalPerks.Shared.Contracts;

public class HubMutation
{
    public int ProtocolVersion { get; set; }
    public string SeasonId { get; set; } = "";
    public long PackRevision { get; set; }
    public string OperationId { get; set; } = "";
    public long ExpectedRevision { get; set; }
    public string RewardId { get; set; } = "";
    public bool UseClassified { get; set; }
    public string DocumentId { get; set; } = "";
    public bool Crate { get; set; }
    public Dictionary<string, int> Sources { get; set; } = new();
    public string RaidId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public int Count { get; set; }
    public bool Split { get; set; }
    public bool PickedUp { get; set; }
}
