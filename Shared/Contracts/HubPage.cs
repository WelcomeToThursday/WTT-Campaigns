using System;

namespace SeasonalPerks.Shared.Contracts;

public sealed class HubPage
{
    public int PreviousRequirement { get; set; } = 0;
    public HubReward[] Rewards { get; set; } = Array.Empty<HubReward>();
}
