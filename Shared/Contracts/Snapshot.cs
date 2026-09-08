using SeasonalPerks.Shared.Configuration;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Profiles;

namespace SeasonalPerks.Shared.Contracts;

public class Snapshot
{
    public int ProtocolVersion { get; set; }
    public string SeasonId { get; set; } = "";
    public string SeasonName { get; set; } = "Season One";
    public List<SeasonChoice> Seasons { get; set; } = new();
    public string SelectedCharacterId { get; set; } = "";
    public long PackRevision { get; set; }
    public string BannerImage { get; set; } = "";
    public bool LegacyBranding { get; set; } = true;
    public bool HasStory { get; set; }
    public List<string> DocumentTemplates { get; set; } = new();
    public string EffectiveProfileId { get; set; } = "";
    public Catalogue Catalogue { get; set; } = new();
    public Dictionary<string, string> Locale { get; set; } = new();
    public Dictionary<string, string> Unavailable { get; set; } = new();
    public Rules Rules { get; set; } = new();
    public PerkState State { get; set; } = new();
    public string ActiveMode { get; set; } = "normal";
    public string? Error { get; set; }
}

// Each side uses its native appearance model while sharing the rest of the response contract.
public class Snapshot<TVisual> : Snapshot
    where TVisual : class
{
    public List<CharacterSummary<TVisual>> Characters { get; set; } = new();
}
