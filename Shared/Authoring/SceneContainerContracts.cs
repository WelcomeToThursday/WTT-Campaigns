using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Shared.Authoring;

public class SceneContainerRequest
{
    public string SessionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string RunId { get; set; } = "";
}

public sealed class SceneContainerResponse
{
    public string? Error { get; set; }
    public List<string> Templates { get; set; } = new();
    public Dictionary<string, List<NativeItem>> Contents { get; set; } = new();
}
