using SPTarkov.Server.Core.Models.Utils;

namespace SeasonalPerks.Server.Hub;

public record HubRequest : IRequestData
{
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
