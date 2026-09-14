using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring;

internal static class RaidEditorAiTree
{
    private const string EncounterGroup = "group:encounters";
    private const string SpawnGroup = "group:spawn-points";
    private const string PatrolGroup = "group:patrol-routes";

    internal static EditorTreeModel Build(MapLayout? layout, string search, ISet<string>? expanded = null)
    {
        var roots = new List<EditorTreeNode>
        {
            new(EncounterGroup, "ENCOUNTERS"),
            new(SpawnGroup, "SPAWN POINTS"),
            new(PatrolGroup, "PATROL ROUTES"),
        };

        if (layout != null)
        {
            AddEncounters(roots[0], layout);
            AddSpawns(roots[1], layout);
            AddPatrols(roots[2], layout);
        }

        return EditorTreeModel.Create(layout?.Id ?? "", roots, search, expanded);
    }

    private static void AddEncounters(EditorTreeNode group, MapLayout layout)
    {
        foreach (var encounter in layout.Encounters ?? new())
        {
            if (encounter == null)
                continue;
            var encounterNode = Node(
                group,
                "enc:" + encounter.Id,
                "ENCOUNTER · " + Display(encounter.Name, encounter.Id),
                "enc:" + encounter.Id,
                true
            );
            if (encounter.Trigger?.Volume != null)
            {
                Node(
                    encounterNode,
                    "trigger:" + encounter.Id,
                    "TRIGGER · " + Display(encounter.Trigger.Type, MapEncounterTrigger.PlayerEntry),
                    "trigger:" + encounter.Id,
                    true
                );
            }

            foreach (var wave in encounter.Waves ?? new())
            {
                if (wave == null)
                    continue;
                var waveNode = Node(
                    encounterNode,
                    "wave:" + encounter.Id + ":" + wave.Id,
                    "WAVE · " + Display(wave.Name, wave.Id),
                    "wave:" + encounter.Id + ":" + wave.Id,
                    true
                );
                foreach (var roster in wave.Roster ?? new())
                {
                    if (roster == null)
                        continue;
                    Node(
                        waveNode,
                        "roster:" + encounter.Id + ":" + wave.Id + ":" + roster.Id,
                        "ROSTER · " + RoleDisplay(roster.Role) + " ×" + roster.Count,
                        "roster:" + encounter.Id + ":" + wave.Id + ":" + roster.Id,
                        true
                    );
                }
            }
        }
    }

    private static void AddSpawns(EditorTreeNode group, MapLayout layout)
    {
        foreach (var spawn in layout.SpawnPoints ?? new())
        {
            if (spawn == null)
                continue;
            Node(group, "spawn:" + spawn.Id, "SPAWN · " + Display(spawn.Name, "Spawn point"), "spawn:" + spawn.Id, true);
        }
    }

    private static void AddPatrols(EditorTreeNode group, MapLayout layout)
    {
        foreach (var route in layout.PatrolRoutes ?? new())
        {
            if (route == null)
                continue;
            var routeNode = Node(
                group,
                "route:" + route.Id,
                "PATROL · " + Display(route.Name, route.Id),
                "route:" + route.Id,
                true
            );
            foreach (var waypoint in route.Waypoints ?? new())
            {
                if (waypoint == null)
                    continue;
                Node(
                    routeNode,
                    "waypoint:" + route.Id + ":" + waypoint.Id,
                    "WAYPOINT · " + Display(waypoint.Name, "Waypoint"),
                    "waypoint:" + route.Id + ":" + waypoint.Id,
                    true
                );
            }
        }
    }

    private static EditorTreeNode Node(EditorTreeNode parent, string key, string label, string id, bool selectable)
    {
        var node = new EditorTreeNode(key, label, id, selectable, parent.Depth + 1, parent.Path + " / " + label);
        parent.Children.Add(node);
        return node;
    }

    private static string Display(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string RoleDisplay(string? role) =>
        role switch
        {
            "assault" => "Scav",
            "pmcUSEC" => "USEC",
            "pmcBEAR" => "BEAR",
            null or "" => "Unsupported",
            _ => "Unsupported · " + role,
        };
}
