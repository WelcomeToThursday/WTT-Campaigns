using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItemUpdateFireMode : NativeModel
{
    [JsonProperty("FireMode", NullValueHandling = NullValueHandling.Ignore)]
    public string? FireMode { get; set; }
}
