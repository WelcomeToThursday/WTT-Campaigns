using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeQuestRewardsInfoIllustrationConfig : NativeModel
{
    [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    [JsonProperty("bigImage", NullValueHandling = NullValueHandling.Ignore)]
    public string? BigImage { get; set; }

    [JsonProperty("isBigImage", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsBigImage { get; set; }
}
