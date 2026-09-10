namespace WTT.Campaigns.Shared.Configuration;

public sealed class Rules
{
    public int StartingPoints { get; set; }
    public bool EnforceBudget { get; set; } = true;
    public bool AllowEdits { get; set; } = true;
    public List<string> EnabledCommonIds { get; set; } = new();
}
