using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeOffer : NativeModel
{
    [JsonProperty("Target", NullValueHandling = NullValueHandling.Ignore)]
    public string Target { get; set; } = "";

    [JsonProperty("TraderId", NullValueHandling = NullValueHandling.Ignore)]
    public string TraderId { get; set; } = "";

    [JsonProperty("Barter", NullValueHandling = NullValueHandling.Ignore)]
    public List<List<NativeBarter>> Barter { get; set; } = new();

    [JsonProperty("Loyalty", NullValueHandling = NullValueHandling.Ignore)]
    public int Loyalty { get; set; }
}
