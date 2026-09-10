using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItemUpdateRepairable : NativeModel
{
    [JsonProperty("Durability", NullValueHandling = NullValueHandling.Ignore)]
    public int? Durability { get; set; }

    [JsonProperty("MaxDurability", NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxDurability { get; set; }
}
