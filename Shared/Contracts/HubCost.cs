using System;

namespace WTT.Campaigns.Shared.Contracts;

public sealed class HubCost
{
    public string DocumentId { get; set; } = "";
    public int Count { get; set; } = 0;
}
