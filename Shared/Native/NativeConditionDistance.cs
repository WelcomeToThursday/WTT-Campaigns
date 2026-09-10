using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeConditionDistance : NativeModel
{
    [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
    public double? Value { get; set; }

    [JsonProperty("compareMethod", NullValueHandling = NullValueHandling.Ignore)]
    public string? CompareMethod { get; set; }
}
