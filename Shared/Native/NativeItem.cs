using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeItem : NativeModel
{
    [JsonProperty("_id", NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; } = "";

    [JsonProperty("_tpl", NullValueHandling = NullValueHandling.Ignore)]
    public string Template { get; set; } = "";

    [JsonProperty("upd", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemUpdate? Upd { get; set; }

    [JsonProperty("parentId", NullValueHandling = NullValueHandling.Ignore)]
    public string? ParentId { get; set; }

    [JsonProperty("slotId", NullValueHandling = NullValueHandling.Ignore)]
    public string? SlotId { get; set; }

    [JsonProperty("location", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemLocation? Location { get; set; }

    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string? Type { get; set; }

    [JsonProperty("count", NullValueHandling = NullValueHandling.Ignore)]
    public int? Count { get; set; }
}
