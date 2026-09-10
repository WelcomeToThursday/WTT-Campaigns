using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

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
