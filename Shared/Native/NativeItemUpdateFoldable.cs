using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItemUpdateFoldable : NativeModel
{
    [JsonProperty("Folded", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Folded { get; set; }
}
