using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeQuestNotes : NativeModel
{
    [JsonProperty("Started", NullValueHandling = NullValueHandling.Ignore)]
    public string? Started { get; set; }

    [JsonProperty("Success", NullValueHandling = NullValueHandling.Ignore)]
    public string? Success { get; set; }

    [JsonProperty("Fail", NullValueHandling = NullValueHandling.Ignore)]
    public string? Fail { get; set; }
}
