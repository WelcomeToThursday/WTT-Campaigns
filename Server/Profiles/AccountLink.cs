namespace SeasonalPerks.Server.Profiles;

public sealed class AccountLink
{
    public HashSet<string> ActiveRaidProfiles { get; set; } = [];
    public string? SeasonalId { get; set; }
    public bool Created { get; set; }
    public string Mode { get; set; } = "normal";
}
