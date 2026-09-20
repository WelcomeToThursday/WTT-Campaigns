namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Small route editing operations that keep per-waypoint waits aligned.</summary>
public static class MapPatrolRouteEditing
{
    public static void Insert(MapPatrolRoute route, int index, SpatialCapture waypoint)
    {
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        if (waypoint == null)
            throw new ArgumentNullException(nameof(waypoint));
        route.Waypoints ??= new();
        if (index < 0 || index > route.Waypoints.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var count = route.Waypoints.Count;
        route.Waypoints.Insert(index, waypoint);
        route.WaitSeconds ??= new();
        if (count > 0 && route.WaitSeconds.Count == count)
            route.WaitSeconds.Insert(index, 0);
    }

    public static bool Move(MapPatrolRoute route, int from, int to)
    {
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        var points = route.Waypoints;
        if (points == null || from < 0 || to < 0 || from >= points.Count || to >= points.Count || from == to)
            return false;
        var point = points[from];
        points.RemoveAt(from);
        points.Insert(to, point);
        if (route.WaitSeconds?.Count == points.Count)
        {
            var wait = route.WaitSeconds[from];
            route.WaitSeconds.RemoveAt(from);
            route.WaitSeconds.Insert(to, wait);
        }
        return true;
    }

    public static void Reverse(MapPatrolRoute route)
    {
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        route.Waypoints.Reverse();
        route.WaitSeconds?.Reverse();
    }

    public static void Append(MapPatrolRoute route, SpatialCapture waypoint)
    {
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        if (waypoint == null)
            throw new ArgumentNullException(nameof(waypoint));

        var previousCount = route.Waypoints?.Count ?? 0;
        route.Waypoints ??= new();
        route.Waypoints.Add(waypoint);
        route.WaitSeconds ??= new();
        if (route.WaitSeconds.Count == previousCount && previousCount > 0)
            route.WaitSeconds.Add(0);
    }

    public static bool RemoveAt(MapPatrolRoute route, int index)
    {
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        if (route.Waypoints == null || index < 0 || index >= route.Waypoints.Count)
            return false;

        var previousCount = route.Waypoints.Count;
        route.Waypoints.RemoveAt(index);
        if (route.WaitSeconds?.Count == previousCount)
            route.WaitSeconds.RemoveAt(index);
        return true;
    }
}
