using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeQuestNotes : NativeModel
{
    [JsonProperty("Started", NullValueHandling = NullValueHandling.Ignore)]
    public string? Started { get; set; }

    [JsonProperty("Success", NullValueHandling = NullValueHandling.Ignore)]
    public string? Success { get; set; }

    [JsonProperty("Fail", NullValueHandling = NullValueHandling.Ignore)]
    public string? Fail { get; set; }
}
