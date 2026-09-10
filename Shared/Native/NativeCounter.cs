using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeCounter : NativeModel
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; } = "";

    [JsonProperty("conditions", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeCondition> Conditions { get; set; } = new();
}
