namespace SeasonalPerks.Shared.Contracts;

public sealed class HubRequirement
{
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
    public int Required { get; set; }
    public int Current { get; set; }
    public bool Met { get; set; }
    public string UnavailableReason { get; set; } = "";
}
