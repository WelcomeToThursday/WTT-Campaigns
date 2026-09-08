using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeBarter : NativeModel
{
    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string? Type { get; set; }

    [JsonProperty("count", NullValueHandling = NullValueHandling.Ignore)]
    public double Count { get; set; }

    [JsonProperty("_tpl", NullValueHandling = NullValueHandling.Ignore)]
    public string Template { get; set; } = "";
}
