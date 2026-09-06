using Newtonsoft.Json;

namespace SeasonalPerks.Shared;

public sealed class Catalogue
{
    [JsonProperty("common")]
    public List<Perk> Common { get; set; } = new();

    [JsonProperty("personal")]
    public List<Perk> Personal { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<Perk> All => Common.Concat(Personal);
}

public sealed class Perk : ExtensibleJsonModel
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("imageUrl")]
    public string ImageUrl { get; set; } = "";

    [JsonProperty("points")]
    public int? Points { get; set; }

    [JsonProperty("effects")]
    public List<PerkEffect> Effects { get; set; } = new();

    [JsonProperty("mutuallyExclusiveSeasonalPerkIds")]
    public List<string> Conflicts { get; set; } = new();
}

public sealed class Rules
{
    public int StartingPoints { get; set; }
    public bool EnforceBudget { get; set; } = true;
    public bool AllowEdits { get; set; } = true;
    public List<string> EnabledCommonIds { get; set; } = new();
}

public sealed class PerkState
{
    public string? RootAccountId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public List<string> SeasonalPerks { get; set; } = new();
    public EffectParameters SeasonalPerkEffectParameters { get; set; } = new();
    public HashSet<string> AppliedGrants { get; set; } = new();
    public Dictionary<string, long> MailNextDue { get; set; } = new();
}

public sealed class CharacterSummary<TVisual>
    where TVisual : class
{
    public string Mode { get; set; } = "normal";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public bool Exists { get; set; }
    public string Side { get; set; } = "Usec";
    public TVisual? Visual { get; set; }
}

public class Snapshot
{
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

public sealed class Mutation
{
    public long ExpectedRevision { get; set; }
    public List<string> PerkIds { get; set; } = new();
    public string Mode { get; set; } = "normal";
    public string Nickname { get; set; } = "Seasonal";
    public string Side { get; set; } = "Usec";
    public string HeadId { get; set; } = "";
    public string VoiceId { get; set; } = "";
}
