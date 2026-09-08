using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeQuestConditions : NativeModel
{
    [JsonProperty("AvailableForStart", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeCondition> AvailableForStart { get; set; } = new();

    [JsonProperty("AvailableForFinish", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeCondition> AvailableForFinish { get; set; } = new();

    [JsonProperty("Fail", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeCondition> Fail { get; set; } = new();

    [JsonProperty("AutoStart", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? AutoStart { get; set; }
}
