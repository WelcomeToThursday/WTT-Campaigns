using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeConditionDaytime : NativeModel
{
    [JsonProperty("from", NullValueHandling = NullValueHandling.Ignore)]
    public int? From { get; set; }

    [JsonProperty("to", NullValueHandling = NullValueHandling.Ignore)]
    public int? To { get; set; }
}
