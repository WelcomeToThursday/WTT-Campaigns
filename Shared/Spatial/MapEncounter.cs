namespace WTT.Campaigns.Shared.Spatial;

/// <summary>AI records authored inside a map layout.</summary>
public sealed class MapEncounter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New encounter";
    public MapEncounterTrigger Trigger { get; set; } = new();
    public List<MapEncounterWave> Waves { get; set; } = new();
}

/// <summary>How an encounter is admitted for one editor run.</summary>
public sealed class MapEncounterTrigger
{
    public const string PlayerEntry = "PlayerEntry";
    public const string MissionStart = "MissionStart";
    public const string Event = "Event";

    public string Type { get; set; } = PlayerEntry;

    // A trigger may own its volume. ZoneId remains useful for a checkpoint or shared zone reference.
    public MapVolume? Volume { get; set; }
    public string ZoneId { get; set; } = "";
    public string EventId { get; set; } = "";
}

public sealed class MapEncounterWave
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Wave";
    public float DelaySeconds { get; set; }
    public bool WaitForPreviousWave { get; set; }
    public List<MapEncounterRosterEntry> Roster { get; set; } = new();
}

public sealed class MapEncounterRosterEntry
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "assault";
    public string Difficulty { get; set; } = "normal";
    public int Count { get; set; } = 1;

    // An encounter-local author label. Empty means an independent roster group.
    public string SquadId { get; set; } = "";
    public List<string> SpawnPointIds { get; set; } = new();
    public string PatrolRouteId { get; set; } = "";
}

public sealed class MapPatrolRoute
{
    public const string Walk = "Walk";
    public const string Run = "Run";
    public const string Loop = "Loop";
    public const string PingPong = "PingPong";
    public const string Stop = "Stop";

    public string Id { get; set; } = "";
    public string Name { get; set; } = "New patrol route";
    public List<SpatialCapture> Waypoints { get; set; } = new();
    public SpatialSpline? Spline { get; set; }

    public bool ShouldSerializeSpline() => Spline != null;

    public string Pace { get; set; } = Walk;
    public string Completion { get; set; } = Loop;

    // Empty means zero seconds at every waypoint. Otherwise one entry is required per waypoint.
    public List<float> WaitSeconds { get; set; } = new();
}
