using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeReward : NativeModel
{
    [JsonProperty("index", NullValueHandling = NullValueHandling.Ignore)]
    public int? Index { get; set; }

    [JsonProperty("unknown", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Unknown { get; set; }

    [JsonProperty("gameMode", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? GameMode { get; set; }

    [JsonProperty("availableInGameEditions", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? AvailableInGameEditions { get; set; }

    [JsonProperty("illustrationConfig", NullValueHandling = NullValueHandling.Ignore)]
    public NativeRewardIllustrationConfig? IllustrationConfig { get; set; }

    [JsonProperty("isHidden", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsHidden { get; set; }

    [JsonProperty("isImportant", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsImportant { get; set; }

    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public string? Id { get; set; }

    [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
    public string? Target { get; set; }

    [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
    public double? Value { get; set; }

    [JsonProperty("isEncoded", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsEncoded { get; set; }

    [JsonProperty("findInRaid", NullValueHandling = NullValueHandling.Ignore)]
    public bool? FindInRaid { get; set; }

    [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeItem> Items { get; set; } = new();

    [JsonProperty("isDeliverByMail", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsDeliverByMail { get; set; }

    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string Type { get; set; } = "";

    [JsonProperty("loyaltyLevel", NullValueHandling = NullValueHandling.Ignore)]
    public int? LoyaltyLevel { get; set; }

    [JsonProperty("traderId", NullValueHandling = NullValueHandling.Ignore)]
    public string? TraderId { get; set; }
}
