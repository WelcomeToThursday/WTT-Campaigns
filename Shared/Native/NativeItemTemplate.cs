using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeItemTemplate : NativeModel
{
    [JsonProperty("_id", NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; } = "";

    [JsonProperty("_name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name { get; set; }

    [JsonProperty("_parent", NullValueHandling = NullValueHandling.Ignore)]
    public string? Parent { get; set; }

    [JsonProperty("_type", NullValueHandling = NullValueHandling.Ignore)]
    public string? Type { get; set; }

    [JsonProperty("_props", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemProperties Properties { get; set; } = new();

    [JsonProperty("_proto", NullValueHandling = NullValueHandling.Ignore)]
    public string? _proto { get; set; }
}
