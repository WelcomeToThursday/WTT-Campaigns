using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeConditionDistance : NativeModel
{
    [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
    public double? Value { get; set; }

    [JsonProperty("compareMethod", NullValueHandling = NullValueHandling.Ignore)]
    public string? CompareMethod { get; set; }
}
