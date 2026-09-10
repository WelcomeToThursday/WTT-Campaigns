using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Effects;

public sealed class ItemFilterRule : ExtensibleJsonModel
{
    [JsonProperty("field")]
    public string Field { get; set; } = "";

    [JsonProperty("value")]
    public string? Value { get; set; } = "";
}
