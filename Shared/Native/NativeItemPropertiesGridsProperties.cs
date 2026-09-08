using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeItemPropertiesGridsProperties : NativeModel
{
    [JsonProperty("filters", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeItemPropertiesGridsPropertiesFilters>? Filters { get; set; }

    [JsonProperty("cellsH", NullValueHandling = NullValueHandling.Ignore)]
    public int? CellsH { get; set; }

    [JsonProperty("cellsV", NullValueHandling = NullValueHandling.Ignore)]
    public int? CellsV { get; set; }

    [JsonProperty("minCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? MinCount { get; set; }

    [JsonProperty("maxCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxCount { get; set; }

    [JsonProperty("maxWeight", NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxWeight { get; set; }

    [JsonProperty("isSortingTable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsSortingTable { get; set; }
}
