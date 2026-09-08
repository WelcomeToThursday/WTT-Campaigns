using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeQuestRewardsInfoItemsUpdRepairable : NativeModel
{
    [JsonProperty("Durability", NullValueHandling = NullValueHandling.Ignore)]
    public int? Durability { get; set; }

    [JsonProperty("MaxDurability", NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxDurability { get; set; }
}
