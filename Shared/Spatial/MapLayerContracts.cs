using WTT.Campaigns.Shared.Native;

namespace WTT.Campaigns.Shared.Spatial;

public class MapLayerRequest
{
    public string CharacterId { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public string Location { get; set; } = "";
}

public sealed class MapLayerResponse
{
    public string CharacterId { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public string Location { get; set; } = "";
    public MapLayout? Layout { get; set; }
    public Dictionary<string, List<NativeItem>> ContainerLoot { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class MapLayerPreferences
{
    public long Revision { get; set; }
    public Dictionary<string, bool> Overrides { get; set; } = new();
}

public class MapLayerOptionsRequest
{
    public string CharacterId { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Enabled { get; set; }
    public long Revision { get; set; }
}

public sealed class MapLayerOption
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Campaign { get; set; } = "";
    public string Location { get; set; } = "";
    public bool Enabled { get; set; }
}

public sealed class MapLayerOptionsResponse
{
    public string CharacterId { get; set; } = "";
    public long Revision { get; set; }
    public List<MapLayerOption> Layers { get; set; } = new();
    public string? Error { get; set; }
}
