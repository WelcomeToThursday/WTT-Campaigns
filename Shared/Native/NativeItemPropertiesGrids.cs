using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItemPropertiesGrids : NativeModel
{
    [JsonProperty("_name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name { get; set; }

    [JsonProperty("_id", NullValueHandling = NullValueHandling.Ignore)]
    public string? Id { get; set; }

    [JsonProperty("_parent", NullValueHandling = NullValueHandling.Ignore)]
    public string? Parent { get; set; }

    [JsonProperty("_props", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemPropertiesGridsProperties? Properties { get; set; }

    [JsonProperty("_proto", NullValueHandling = NullValueHandling.Ignore)]
    public string? _proto { get; set; }
}
