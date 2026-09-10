using System;

namespace WTT.Campaigns.Shared.Contracts;

public sealed class HubReward
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Side { get; set; } = "";
    public string Image { get; set; } = "";
    public string BigImage { get; set; } = "";
    public int X { get; set; } = 0;
    public int Y { get; set; } = 0;
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public HubCost[] Costs { get; set; } = Array.Empty<HubCost>();
    public string[] Requirements { get; set; } = Array.Empty<string>();
    public HubRequirement[] Eligibility { get; set; } = Array.Empty<HubRequirement>();
    public bool Claimed { get; set; } = false;

    public bool CanClaim { get; set; } = false;
    public string UnavailableReason { get; set; } = "";
    public int UniversalNeeded { get; set; } = 0;
}
