using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItemUpdate : NativeModel
{
    [JsonProperty("StackObjectsCount", NullValueHandling = NullValueHandling.Ignore)]
    public double? StackObjectsCount { get; set; }

    [JsonProperty("Repairable", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemUpdateRepairable? Repairable { get; set; }

    [JsonProperty("SpawnedInSession", NullValueHandling = NullValueHandling.Ignore)]
    public bool? SpawnedInSession { get; set; }

    [JsonProperty("FireMode", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemUpdateFireMode? FireMode { get; set; }

    [JsonProperty("Foldable", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemUpdateFoldable? Foldable { get; set; }
}
