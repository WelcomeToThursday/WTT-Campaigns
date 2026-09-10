using Newtonsoft.Json;

namespace WTT.Campaigns.Client.Story;

internal sealed class TraderMediaManifest
{
    [JsonProperty("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonProperty("rooms")]
    public List<TraderMediaRoom> Rooms { get; set; } = new();
}

internal sealed class TraderMediaRoom
{
    [JsonProperty("trader")]
    public string Trader { get; set; } = "";

    [JsonProperty("bundle")]
    public string Bundle { get; set; } = "";

    [JsonProperty("sha256")]
    public string Sha256 { get; set; } = "";
}
