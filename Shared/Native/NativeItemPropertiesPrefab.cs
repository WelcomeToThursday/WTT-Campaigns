using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeItemPropertiesPrefab : NativeModel
{
    [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
    public string? Path { get; set; }

    [JsonProperty("rcid", NullValueHandling = NullValueHandling.Ignore)]
    public string? Rcid { get; set; }
}
