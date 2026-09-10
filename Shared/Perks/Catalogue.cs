using Newtonsoft.Json;

namespace WTT.Campaigns.Shared.Perks;

public sealed class Catalogue
{
    [JsonProperty("common")]
    public List<Perk> Common { get; set; } = new();

    [JsonProperty("personal")]
    public List<Perk> Personal { get; set; } = new();

    [JsonIgnore]
    public IEnumerable<Perk> All
    {
        get { return Common.Concat(Personal); }
    }
}
