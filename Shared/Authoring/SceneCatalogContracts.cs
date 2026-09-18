using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Shared.Authoring;

public class SceneCatalogRequest
{
    public int Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string Search { get; set; } = "";
    public string Category { get; set; } = "Items";
    public string Id { get; set; } = "";
    public List<string>? TemplateIds { get; set; }
    public int Page { get; set; }
}

public sealed class SceneCatalogEntry
{
    public string Error { get; set; } = "";
    public WTT.Campaigns.Shared.Spatial.MapTarget? AssetTarget { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? KeyId { get; set; }

    public bool ShouldSerializeKeyId() => KeyId != null;

    public List<NativeItem> Items { get; set; } = new();
}

public sealed class SceneCatalogResponse
{
    public string? Error { get; set; }
    public int Total { get; set; }
    public List<SceneCatalogEntry> Entries { get; set; } = new();
}
