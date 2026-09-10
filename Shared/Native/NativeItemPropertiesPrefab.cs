using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItemPropertiesPrefab : NativeModel
{
    [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
    public string? Path { get; set; }

    [JsonProperty("rcid", NullValueHandling = NullValueHandling.Ignore)]
    public string? Rcid { get; set; }
}
