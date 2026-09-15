using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Shared.Authoring;

public class EditorPreviewGearRequest
{
    public int Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
}

public sealed class EditorPreviewGearResponse
{
    public List<EditorPreviewGearSlot> Slots { get; set; } = new();
    public Dictionary<string, string> FastPanel { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class EditorPreviewGearSlot
{
    public string Slot { get; set; } = "";
    public List<NativeItem> Items { get; set; } = new();
}
