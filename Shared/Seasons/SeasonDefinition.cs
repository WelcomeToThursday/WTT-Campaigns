using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Configuration;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Serialization;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Shared.Seasons;

// These are authoring contracts. Player state is constructed by the runtime compiler only.
public sealed class SeasonDefinition : ExtensibleJsonModel
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string BattlePassId { get; set; } = "";
    public string Name { get; set; } = "New season";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public long Revision { get; set; }
    public bool Legacy { get; set; }
    public SeasonBranding Branding { get; set; } = new();
    public Rules Rules { get; set; } = new();
    public Catalogue Perks { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Locales { get; set; } = new() { ["en"] = new() };
    public List<SeasonDocument> Documents { get; set; } = new();
    public SeasonCollection Collection { get; set; } = new();
    public List<SeasonPage> Pages { get; set; } = new();
    public List<SeasonReward> SeasonalRewards { get; set; } = new();
    public List<HubSlide> Slides { get; set; } = new();
    public string UniversalImage { get; set; } = "";
    public string UniversalUnavailableImage { get; set; } = "";
    public int ExchangeRate { get; set; } = 5;
    public string ExchangeCrate { get; set; } = "";
    public int CrateCost { get; set; } = 10;
    public List<SeasonItem> Items { get; set; } = new();
    public List<SeasonCrate> Crates { get; set; } = new();
    public JArray Quests { get; set; } = new();

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public StoryDefinition? Story { get; set; }
    public JArray Offers { get; set; } = new();
    public JObject ImportedItems { get; set; } = new();
    public SeasonStartingSetup Starting { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<SeasonReward> AllRewards
    {
        get { return Pages.SelectMany(p => p.Rewards).Concat(SeasonalRewards); }
    }
}

public sealed class SeasonBranding : ExtensibleJsonModel
{
    public string Badge { get; set; } = "";
    public string Banner { get; set; } = "";
}

public sealed class SeasonCollection : ExtensibleJsonModel
{
    public int DocumentsPerRaid { get; set; } = 8;
    public int ClassifiedChancePercent { get; set; } = 5;
    public int DocumentLimit { get; set; } = 30;
    public int WindowSeconds { get; set; } = 23 * 60 * 60;
    public Dictionary<string, int> MapCounts { get; set; } = new();
}

public sealed class SeasonDocument : ExtensibleJsonModel
{
    public string Id { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "Document";
    public string Image { get; set; } = "";
    public string UnavailableImage { get; set; } = "";
}

public sealed class SeasonPage : ExtensibleJsonModel
{
    public int PreviousRequirement { get; set; }
    public List<SeasonReward> Rewards { get; set; } = new();
}

public sealed class SeasonReward : ExtensibleJsonModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Reward";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "ITEM";
    public string Side { get; set; } = "";
    public string Image { get; set; } = "";
    public string BigImage { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public List<HubCost> Costs { get; set; } = new();
    public List<string> Requirements { get; set; } = new();
    public JArray Grants { get; set; } = new();
    public JArray Conditions { get; set; } = new();
}

public sealed class SeasonItem : ExtensibleJsonModel
{
    public string Id { get; set; } = "";
    public string CloneFrom { get; set; } = "";
    public string Name { get; set; } = "Season item";
    public string Description { get; set; } = "";
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public int StackMax { get; set; } = 999;
}

public sealed class SeasonCrate : ExtensibleJsonModel
{
    public string ItemId { get; set; } = "";
    public int RewardCount { get; set; } = 1;
    public bool FoundInRaid { get; set; }
    public Dictionary<string, double> Pool { get; set; } = new();
}

public sealed class SeasonStartingSetup : ExtensibleJsonModel
{
    public string Preset { get; set; } = "";
    public FactionStartingSetup Usec { get; set; } = new();
    public FactionStartingSetup Bear { get; set; } = new();
}

public sealed class FactionStartingSetup : ExtensibleJsonModel
{
    public List<StartingItem> Items { get; set; } = new();
    public Dictionary<string, int> Skills { get; set; } = new();
}

public sealed class StartingItem : ExtensibleJsonModel
{
    public string Template { get; set; } = "";
    public int Count { get; set; } = 1;

    // Empty means stash; an equipment slot replaces that slot's starter item tree.
    public string Slot { get; set; } = "";
}

public sealed class SeasonManifest : ExtensibleJsonModel
{
    public int FormatVersion { get; set; } = 1;
    public string SeasonId { get; set; } = "";
    public string BattlePassId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public long Revision { get; set; }
    public string GameplayHash { get; set; } = "";
    public int ProtocolVersion { get; set; } = 2;
    public string SptVersion { get; set; } = "~4.1.3";
    public string ModVersion { get; set; } = "~0.3.0";
    public List<string> Dependencies { get; set; } = new();
    public Dictionary<string, string> Files { get; set; } = new();
}

public sealed class SeasonValidationIssue
{
    public string Path { get; set; } = "";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "error";
}

public sealed class SeasonValidationResult
{
    public List<SeasonValidationIssue> Issues { get; set; } = new();
    public bool CanPublish
    {
        get { return Issues.All(i => i.Severity != "error"); }
    }

    public bool CanActivate
    {
        get { return CanPublish && Issues.All(i => i.Severity != "dependency"); }
    }

    public void Add(string path, string message, string severity = "error")
    {
        Issues.Add(
            new()
            {
                Path = path,
                Message = message,
                Severity = severity,
            }
        );
    }
}
