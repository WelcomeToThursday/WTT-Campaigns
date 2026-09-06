using Newtonsoft.Json;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Perks;

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
