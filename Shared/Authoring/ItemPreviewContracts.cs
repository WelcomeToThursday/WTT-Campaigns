using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Shared.Authoring;

public sealed class ItemPreviewExchange
{
    public const int Protocol = 1;
    public const string Renderer = "assort-1";
    public int Version { get; set; } = Protocol;
    public string Fingerprint { get; set; } = "";
    public bool Ready { get; set; }
    public bool Busy { get; set; }
    public string ActiveJobId { get; set; } = "";
    public ItemPreviewJob? Job { get; set; }
    public ItemPreviewResult? Result { get; set; }
}

public sealed class ItemPreviewJob
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public List<NativeItem> Items { get; set; } = new();
}

public sealed class ItemPreviewResult
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public string Png { get; set; } = "";
    public bool Verified { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<PreviewItemSize> ItemSizes { get; set; } = new();
}

public sealed class PreviewItemSize
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class OfferContainer
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "Slot";
    public bool Required { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Capacity { get; set; } = 1;
    public List<string> Allowed { get; set; } = new();
    public List<List<string>> AllowedGroups { get; set; } = new();
    public List<string> Excluded { get; set; } = new();
}

public sealed class OfferItemInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Parent { get; set; } = "";
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public int StackMax { get; set; } = 1;
    public string Hash { get; set; } = "";
    public List<string> Conflicts { get; set; } = new();
    public List<OfferContainer> Containers { get; set; } = new();
}
