using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

// One anchor per mutually reachable island, in stable order. Reachability is
// supplied by the live carved NavMesh; proximity alone never connects islands.
internal static class EncounterCorePlan
{
    internal static int Allocate(HashSet<int> used)
    {
        for (var id = 1; id < int.MaxValue; id++)
            if (used.Add(id))
                return id;
        throw new InvalidOperationException("No unused native AI identity is available.");
    }

    internal static List<SpatialCapture> Anchors(
        IEnumerable<SpatialCapture> assigned,
        Func<SpatialCapture, bool> hasNativeCore,
        Func<SpatialCapture, SpatialCapture, bool> reaches
    )
    {
        var points = new List<SpatialCapture>(assigned);
        points.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var anchors = new List<SpatialCapture>();
        foreach (var point in points)
        {
            if (hasNativeCore(point))
                continue;
            var connected = false;
            foreach (var anchor in anchors)
                if (reaches(point, anchor) && reaches(anchor, point))
                {
                    connected = true;
                    break;
                }
            if (!connected)
                anchors.Add(point);
        }
        return anchors;
    }

    internal static List<SpatialCapture> Assigned(MapLayout layout)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var encounter in layout.Encounters)
        foreach (var wave in encounter.Waves)
        foreach (var roster in wave.Roster)
        foreach (var id in roster.SpawnPointIds)
            ids.Add(id);
        var points = new List<SpatialCapture>();
        foreach (var point in layout.SpawnPoints)
            if (ids.Contains(point.Id))
                points.Add(point);
        return points;
    }
}
