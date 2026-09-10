namespace WTT.Campaigns.Shared.Profiles;

public sealed class PerkState
{
    public string? SeasonId { get; set; }
    public string? GameplayHash { get; set; }
    public string? RootAccountId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public List<string> SeasonalPerks { get; set; } = new();
    public EffectParameters SeasonalPerkEffectParameters { get; set; } = new();
    public HashSet<string> AppliedGrants { get; set; } = new();
    public Dictionary<string, long> MailNextDue { get; set; } = new();
}
