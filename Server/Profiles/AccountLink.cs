namespace SeasonalPerks.Server.Profiles;

public sealed class AccountLink
{
    public string? CurrentSeasonId { get; set; }
    public Dictionary<string, SeasonCharacterLink> Seasons { get; set; } = new();
    public HashSet<string> ActiveRaidProfiles { get; set; } = [];
    public string? SeasonalId { get; set; }
    public bool Created { get; set; }
    public string Mode { get; set; } = "normal";
}

public sealed class SeasonCharacterLink
{
    public string ProfileId { get; set; } = "";
    public bool Created { get; set; }
}
