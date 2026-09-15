namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Small route editing operations that keep per-waypoint waits aligned.</summary>
public static class MapPatrolRouteEditing
{
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
