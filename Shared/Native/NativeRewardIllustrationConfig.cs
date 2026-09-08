using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeRewardIllustrationConfig : NativeModel
{
    [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    [JsonProperty("bigImage", NullValueHandling = NullValueHandling.Ignore)]
    public string? BigImage { get; set; }

    [JsonProperty("isBigImage", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsBigImage { get; set; }
}
