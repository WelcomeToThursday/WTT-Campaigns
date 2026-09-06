using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Effects;

public sealed class ItemFilterRule : ExtensibleJsonModel
{
    [JsonProperty("field")]
    public string Field { get; set; } = "";

    [JsonProperty("value")]
    public string Value { get; set; } = "";
}
