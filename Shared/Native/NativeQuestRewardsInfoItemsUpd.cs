using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeQuestRewardsInfoItemsUpd : NativeModel
{
    [JsonProperty("SpawnedInSession", NullValueHandling = NullValueHandling.Ignore)]
    public bool? SpawnedInSession { get; set; }

    [JsonProperty("StackObjectsCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? StackObjectsCount { get; set; }

    [JsonProperty("Repairable", NullValueHandling = NullValueHandling.Ignore)]
    public NativeQuestRewardsInfoItemsUpdRepairable? Repairable { get; set; }
}
