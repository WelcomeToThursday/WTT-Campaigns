using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Effects;

public sealed class AllergySubEffect : ExtensibleJsonModel
{
    [JsonProperty("enabled", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Enabled { get; set; }

    [JsonProperty("durationSeconds", NullValueHandling = NullValueHandling.Ignore)]
    public float? DurationSeconds { get; set; }

    [JsonProperty("amount", NullValueHandling = NullValueHandling.Ignore)]
    public float? Amount { get; set; }
}
