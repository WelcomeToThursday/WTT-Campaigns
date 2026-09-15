using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Shared.Authoring;

public class SceneCatalogRequest
{
    public int Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string Search { get; set; } = "";
    public string Category { get; set; } = "Items";
    public string Id { get; set; } = "";
    public int Page { get; set; }
}

public sealed class SceneCatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<NativeItem> Items { get; set; } = new();
}

public sealed class SceneCatalogResponse
{
    public string? Error { get; set; }
    public int Total { get; set; }
    public List<SceneCatalogEntry> Entries { get; set; } = new();
}
