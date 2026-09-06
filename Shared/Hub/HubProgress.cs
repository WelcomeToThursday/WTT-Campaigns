using System.Collections.Generic;

namespace SeasonalPerks.Shared.Hub;

public sealed class HubProgress
{
    public string BattlePassId { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public long Revision { get; set; }
    public HashSet<string> Claimed { get; set; } = new();
    public HashSet<string> UnlockedOffers { get; set; } = new();
    public int Classified { get; set; }
    public long Tarcoins { get; set; }
    public long WindowStart { get; set; }
    public int Pickups { get; set; }
    public Dictionary<string, HubReceipt> Receipts { get; set; } = new();
    public Dictionary<string, HubRaid> Raids { get; set; } = new();
}
