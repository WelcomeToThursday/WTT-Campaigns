using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal readonly struct EditorAiSelection
{
    internal EditorAiSelection(
        string kind,
        MapEncounter? encounter = null,
        MapEncounterWave? wave = null,
        MapEncounterRosterEntry? roster = null,
        SpatialCapture? spawn = null,
        MapPatrolRoute? route = null,
        SpatialCapture? waypoint = null
    )
    {
        Kind = kind;
        Encounter = encounter;
        Wave = wave;
        Roster = roster;
        Spawn = spawn;
        Route = route;
        Waypoint = waypoint;
    }

    internal string Kind { get; }
    internal MapEncounter? Encounter { get; }
    internal MapEncounterWave? Wave { get; }
    internal MapEncounterRosterEntry? Roster { get; }
    internal SpatialCapture? Spawn { get; }
    internal MapPatrolRoute? Route { get; }
    internal SpatialCapture? Waypoint { get; }
    internal MapVolume? TriggerVolume => Encounter?.Trigger?.Volume;
    internal SpatialCapture? Point =>
        Kind switch
        {
            "spawn" => Spawn,
            "waypoint" => Waypoint,
            "trigger" => TriggerVolume,
            _ => null,
        };
    internal string Id =>
        Point?.Id
        ?? (
            Kind == "enc" ? Encounter?.Id
            : Kind == "wave" ? Wave?.Id
            : Kind == "roster" ? Roster?.Id
            : Kind == "route" ? Route?.Id
            : ""
        )
        ?? "";
    internal string Name =>
        Point?.Name
        ?? (
            Kind == "enc" ? Encounter?.Name
            : Kind == "wave" ? Wave?.Name
            : Kind == "roster" ? Roster?.Id
            : Kind == "route" ? Route?.Name
            : ""
        )
        ?? "";

    // The default struct is used when the active layout has no matching
    // selection. Its auto-property backing field is null until a value is
    // assigned, so validity must tolerate the default value.
    internal bool Valid => !string.IsNullOrEmpty(Kind);

    internal static bool TryResolve(MapLayout layout, string selected, out EditorAiSelection selection)
    {
        selection = default;
        var parts = selected.Split(':');
        if (parts.Length < 2)
            return false;

        if (parts[0] == "enc")
        {
            var encounter = (Items(layout.Encounters)).AsValueEnumerable().FirstOrDefault(e => e?.Id == parts[1]);
            if (encounter == null)
                return false;
            selection = new EditorAiSelection("enc", encounter: encounter);
            return true;
        }

        if (parts[0] == "trigger")
        {
            var encounter = (Items(layout.Encounters)).AsValueEnumerable().FirstOrDefault(e => e?.Id == parts[1]);
            if (encounter?.Trigger?.Volume == null)
                return false;
            selection = new EditorAiSelection("trigger", encounter: encounter);
            return true;
        }

        if (parts[0] == "spawn")
        {
            var spawn = (Items(layout.SpawnPoints)).AsValueEnumerable().FirstOrDefault(p => p?.Id == parts[1]);
            if (spawn == null)
                return false;
            selection = new EditorAiSelection("spawn", spawn: spawn);
            return true;
        }

        if (parts[0] == "route")
        {
            var route = (Items(layout.PatrolRoutes)).AsValueEnumerable().FirstOrDefault(r => r?.Id == parts[1]);
            if (route == null)
                return false;
            selection = new EditorAiSelection("route", route: route);
            return true;
        }

        if (parts[0] == "waypoint" && parts.Length >= 3)
        {
            var route = (Items(layout.PatrolRoutes)).AsValueEnumerable().FirstOrDefault(r => r?.Id == parts[1]);
            var waypoint = Items(route?.Waypoints).AsValueEnumerable().FirstOrDefault(p => p?.Id == parts[2]);
            if (route == null || waypoint == null)
                return false;
            selection = new EditorAiSelection("waypoint", route: route, waypoint: waypoint);
            return true;
        }

        var encounterForChild = (Items(layout.Encounters)).AsValueEnumerable().FirstOrDefault(e => e?.Id == parts[1]);
        var wave = Items(encounterForChild?.Waves).AsValueEnumerable().FirstOrDefault(w => w?.Id == (parts.Length >= 3 ? parts[2] : ""));
        if (wave == null)
            return false;
        if (parts[0] == "wave")
        {
            selection = new EditorAiSelection("wave", encounter: encounterForChild, wave: wave);
            return true;
        }

        if (parts[0] == "roster" && parts.Length >= 4)
        {
            var roster = Items(wave.Roster).AsValueEnumerable().FirstOrDefault(r => r?.Id == parts[3]);
            if (roster == null)
                return false;
            selection = new EditorAiSelection("roster", encounter: encounterForChild, wave: wave, roster: roster);
            return true;
        }
        return false;
    }

    internal void Rename(string value)
    {
        var name = value.Trim();
        switch (Kind)
        {
            case "enc":
                if (Encounter != null)
                    Encounter.Name = name;
                break;
            case "wave":
                if (Wave != null)
                    Wave.Name = name;
                break;
            case "roster":
                break;
            case "spawn":
            case "waypoint":
            case "trigger":
                if (Point != null)
                    Point.Name = name;
                break;
            case "route":
                if (Route != null)
                    Route.Name = name;
                break;
        }
    }

    private static IEnumerable<T> Items<T>(IEnumerable<T>? values) => values ?? Array.Empty<T>();
}
