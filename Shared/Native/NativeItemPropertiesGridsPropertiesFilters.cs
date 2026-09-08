using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeItemPropertiesGridsPropertiesFilters : NativeModel
{
    [JsonProperty("ExcludedFilter", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? ExcludedFilter { get; set; }

    [JsonProperty("Filter", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Filter { get; set; }
}
