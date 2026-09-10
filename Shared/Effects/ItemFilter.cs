using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Effects;

public sealed class ItemFilter : ExtensibleJsonModel
{
    [JsonProperty("include", NullValueHandling = NullValueHandling.Ignore)]
    public List<ItemFilterRule>? Include { get; set; }

    [JsonProperty("exclude", NullValueHandling = NullValueHandling.Ignore)]
    public List<ItemFilterRule>? Exclude { get; set; }
}
