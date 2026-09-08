using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeQuestMailSettings : NativeModel
{
    [JsonProperty("isEnabled", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsEnabled { get; set; }

    [JsonProperty("fromTraderId", NullValueHandling = NullValueHandling.Ignore)]
    public string? FromTraderId { get; set; }

    [JsonProperty("entryPoint", NullValueHandling = NullValueHandling.Ignore)]
    public string? EntryPoint { get; set; }

    [JsonProperty("dialogueId", NullValueHandling = NullValueHandling.Ignore)]
    public string? DialogueId { get; set; }

    [JsonProperty("dialogueTraderId", NullValueHandling = NullValueHandling.Ignore)]
    public string? DialogueTraderId { get; set; }

    [JsonProperty("whileAvailableMessageText", NullValueHandling = NullValueHandling.Ignore)]
    public string? WhileAvailableMessageText { get; set; }
}
