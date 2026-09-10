using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItemPropertiesGridsPropertiesFilters : NativeModel
{
    [JsonProperty("ExcludedFilter", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? ExcludedFilter { get; set; }

    [JsonProperty("Filter", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Filter { get; set; }
}
