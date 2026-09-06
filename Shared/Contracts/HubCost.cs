using System;

namespace SeasonalPerks.Shared.Contracts;

public sealed class HubCost
{
    public string DocumentId { get; set; } = "";
    public int Count { get; set; } = 0;
}
