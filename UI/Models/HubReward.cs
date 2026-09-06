using System;

namespace SeasonalPerks.UI.Models;

[Serializable]
public sealed class HubReward
{
    public string Id = "";
    public string Name = "";
    public string Description = "";
    public string Kind = "";
    public string Side = "";
    public string Image = "";
    public string BigImage = "";
    public int X = 0;
    public int Y = 0;
    public int Width = 1;
    public int Height = 1;
    public HubCost[] Costs = Array.Empty<HubCost>();
    public string[] Requirements = Array.Empty<string>();
    public HubRequirement[] Eligibility = Array.Empty<HubRequirement>();
    public bool Claimed = false;

    public bool CanClaim = false;
    public string UnavailableReason = "";
    public int UniversalNeeded = 0;
}
