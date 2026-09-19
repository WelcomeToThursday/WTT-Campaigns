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
        get
        {
            foreach (var perk in Common)
                yield return perk;
            foreach (var perk in Personal)
                yield return perk;
        }
    }
}
