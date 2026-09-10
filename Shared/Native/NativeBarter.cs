using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeBarter : NativeModel
{
    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string? Type { get; set; }

    [JsonProperty("count", NullValueHandling = NullValueHandling.Ignore)]
    public double Count { get; set; }

    [JsonProperty("_tpl", NullValueHandling = NullValueHandling.Ignore)]
    public string Template { get; set; } = "";
}
