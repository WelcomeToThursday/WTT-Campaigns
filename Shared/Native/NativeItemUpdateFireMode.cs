using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeItemUpdateFireMode : NativeModel
{
    [JsonProperty("FireMode", NullValueHandling = NullValueHandling.Ignore)]
    public string? FireMode { get; set; }
}
