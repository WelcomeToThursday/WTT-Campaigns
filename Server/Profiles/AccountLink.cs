namespace WTT.Campaigns.Server.Profiles;

public sealed class AccountLink
{
    public List<SeasonCharacterLink> Characters { get; set; } = new();
    public Dictionary<string, string> RetiredCharacters { get; set; } = new();
    public string? CurrentSeasonId { get; set; }
    public Dictionary<string, SeasonCharacterLink> Seasons { get; set; } = new();
    public HashSet<string> ActiveRaidProfiles { get; set; } = [];
    public Dictionary<string, string> ActiveRaidIds { get; set; } = [];
    public string? SeasonalId { get; set; }
    public bool Created { get; set; }
    public string Mode { get; set; } = "normal";
}

public sealed class SeasonCharacterLink
{
    public string SeasonId { get; set; } = "";
    public string CreationOperationId { get; set; } = "";
    public string CreationFingerprint { get; set; } = "";
    public bool Wiped { get; set; }
    public string Name { get; set; } = "";
    public string WipeOperationId { get; set; } = "";
    public Dictionary<string, long> PreservedAchievements { get; set; } = new();
    public string ProfileId { get; set; } = "";
    public bool Created { get; set; }
}
