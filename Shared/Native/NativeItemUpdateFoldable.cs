using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeItemUpdateFoldable : NativeModel
{
    [JsonProperty("Folded", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Folded { get; set; }
}
