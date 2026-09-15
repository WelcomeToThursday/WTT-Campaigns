using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

// Read-only, local inspection. Never triangulate the whole map or register cores
// while browsing. Expensive queries run at most once every two seconds.
internal sealed class NavigationInspection
{
    internal readonly List<(Vector3 From, Vector3 To, Color Color)> Segments = new();
    internal readonly List<(SpatialCapture Point, string Caption, Color Color)> Points = new();
    internal string Summary = "Select a spawn to inspect navigation.";
    private float _next;
    private MapLayout? _layout;
    private string _selected = "";
    private static readonly Color Good = new(.35f, .85f, .45f);
    private static readonly Color Bad = new(1f, .35f, .3f);
    private static readonly Color Local = new(1f, .75f, .25f);

    internal void Refresh(MapLayout layout, string selected)
    {
        if (ReferenceEquals(layout, _layout) && selected == _selected && Time.realtimeSinceStartup < _next)
            return;
        _layout = layout;
        _selected = selected;
        _next = Time.realtimeSinceStartup + 2;
        Segments.Clear();
        Points.Clear();
        SpatialCapture? spawn = null;
        foreach (var point in layout.SpawnPoints)
            if ("spawn:" + point.Id == selected)
                spawn = point;
        if (spawn == null)
        {
            Summary =
                "NAVIGATION · Select a spawn in the tree.\nGreen: complete path · Amber: local core needed · Red: blocked\nInspection shows a 28 m area around the selected spawn.";
            return;
        }
        try
        {
            var position = EncounterNavigation.ToVector3(spawn.Position);
            var navigation = new EncounterNavigation();
            var valid = navigation.IsOnNavMesh(spawn.Position) && navigation.HasStandingClearance(spawn.Position);
            var cores = AICorePointHolder.GetAllTestObjects(false);
            var connected = EncounterCorePoints.ConnectedCore(cores, position);
            AICorePoint? nearest = null;
            var distance = float.PositiveInfinity;
            foreach (var core in cores)
            {
                if (!core)
                    continue;
                var d = (core.Position - position).sqrMagnitude;
                if (d < distance)
                {
                    nearest = core;
                    distance = d;
                }
            }
            var target = connected ? connected : nearest;
            if (target)
            {
                var path = new NavMeshPath();
                NavMesh.CalculatePath(position, target!.Position, NavMesh.AllAreas, path);
                var corners = path.corners;
                for (var i = 1; i < corners.Length; i++)
                    Segments.Add((corners[i - 1], corners[i], connected ? Good : Bad));
                if (!connected)
                    Segments.Add((corners.Length > 0 ? corners[^1] : position, target.Position, Bad));
                Points.Add((Capture(target.Position), "AI CORE " + target.Id, connected ? Good : Bad));
            }
            Points.Add(
                (
                    spawn,
                    valid
                        ? connected
                            ? "CONNECTED SPAWN"
                            : "LOCAL CORE NEEDED"
                        : "BLOCKED SPAWN",
                    valid
                        ? connected
                            ? Good
                            : Local
                        : Bad
                )
            );
            // Samples at the spawn elevation, not a claim of a full NavMesh view.
            for (var x = -7; x <= 7; x++)
            for (var z = -7; z <= 7; z++)
            {
                var sample = position + new Vector3(x * 2, 0, z * 2);
                var onMesh = NavMesh.SamplePosition(sample, out var hit, .35f, NavMesh.AllAreas);
                var at = (onMesh ? hit.position : sample) + Vector3.up * .08f;
                Segments.Add((at - Vector3.right * .15f, at + Vector3.right * .15f, onMesh ? Good : Bad));
            }
            var obstacleCount = 0;
            SceneNavigation.VisitObstacles(obstacle =>
            {
                if (obstacleCount >= 64 || (obstacle.transform.position - position).sqrMagnitude > 40 * 40)
                    return;
                obstacleCount++;
                var vertices = new Vector3[8];
                for (var i = 0; i < 8; i++)
                    vertices[i] = obstacle.transform.TransformPoint(
                        obstacle.center
                            + Vector3.Scale(
                                obstacle.size * .5f,
                                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)
                            )
                    );
                for (var i = 0; i < 8; i++)
                for (var axis = 1; axis <= 4; axis *= 2)
                    if ((i & axis) == 0)
                        Segments.Add((vertices[i], vertices[i | axis], Local));
            });
            Summary =
                $"NAVIGATION · {spawn.Name}\n"
                + (
                    valid
                        ? connected
                            ? "Complete two-way connection to core " + connected!.Id
                            : "Isolated ground: preview will create a local core."
                        : "Blocked or off NavMesh: move this spawn."
                )
                + "\nGreen: walkable samples / connected path · Red: missing samples / disconnected route"
                + "\nAmber boxes: authored cuts (up to 64 nearby) · Samples at spawn height · Refresh: 2 s";
        }
        catch (Exception error)
        {
            Segments.Clear();
            Points.Clear();
            Summary = "Navigation inspection unavailable: " + error.Message;
        }
    }

    private static SpatialCapture Capture(Vector3 p) =>
        new()
        {
            Position = new SpatialVector
            {
                X = p.x,
                Y = p.y,
                Z = p.z,
            },
        };
}
