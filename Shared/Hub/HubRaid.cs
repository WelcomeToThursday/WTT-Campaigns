using System.Collections.Generic;

namespace WTT.Campaigns.Shared.Hub;

public sealed class HubRaid
{
    public Dictionary<string, string> Spawned { get; set; } = new();
    public HashSet<string> Picked { get; set; } = new();
    public HashSet<string> Rejected { get; set; } = new();
    public Dictionary<string, HubDocumentStack> Stacks { get; set; } = new();
    public Dictionary<string, string> Operations { get; set; } = new();
    public bool Finished { get; set; }
    public int Bonus { get; set; }
}
