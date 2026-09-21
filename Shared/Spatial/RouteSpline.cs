namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Connects reusable spline knots to route records without turning shaping knots into gameplay waypoints.</summary>
public static class RouteSpline
{
    public static List<SpatialCapture> PlayerAnchors(MapLayout layout)
    {
        var result = new List<SpatialCapture>();
        if (layout.Start != null)
            result.Add(layout.Start);
        result.AddRange(layout.Checkpoints);
        if (layout.Exit != null)
            result.Add(layout.Exit);
        return result;
    }

    public static SpatialSpline Create(IReadOnlyList<SpatialCapture> anchors, bool closed)
    {
        var spline = new SpatialSpline { Closed = closed };
        foreach (var a in anchors)
            spline.Knots.Add(Anchor(a));
        return spline;
    }

    private static SplineKnot Anchor(SpatialCapture a) => new() { AnchorId = a.Id, Position = Copy(a.Position) };

    private static SpatialVector Copy(SpatialVector p) =>
        new()
        {
            X = p.X,
            Y = p.Y,
            Z = p.Z,
        };

    public static void Synchronize(MapLayout layout)
    {
        if (layout.PlayerRouteSpline != null)
            Synchronize(layout.PlayerRouteSpline, PlayerAnchors(layout), false);
        foreach (var route in layout.PatrolRoutes)
            if (route.Spline != null)
                Synchronize(route.Spline, route.Waypoints, route.Completion == MapPatrolRoute.Loop);
    }

    public static void Synchronize(SpatialSpline spline, IReadOnlyList<SpatialCapture> anchors, bool closed)
    {
        // Each shaping block belongs to its preceding anchor. Reordering never transfers waits or identities.
        var blocks = new Dictionary<string, List<SplineKnot>>(StringComparer.Ordinal);
        List<SplineKnot>? block = null;
        foreach (var knot in spline.Knots)
        {
            if (knot.AnchorId.Length > 0)
            {
                block = new();
                blocks[knot.AnchorId] = block;
            }
            block?.Add(knot);
        }
        var next = new List<SplineKnot>();
        var changed = spline.Closed != closed;
        for (var index = 0; index < anchors.Count; index++)
        {
            var a = anchors[index];
            if (!blocks.TryGetValue(a.Id, out var existing))
                existing = new() { Anchor(a) };
            var knot = existing[0];
            changed |= knot.Position.X != a.Position.X || knot.Position.Y != a.Position.Y || knot.Position.Z != a.Position.Z;
            knot.Position = Copy(a.Position);
            next.AddRange(existing);
        }
        if (!closed)
            while (next.Count > 0 && next[^1].AnchorId.Length == 0)
                next.RemoveAt(next.Count - 1);
        if (spline.Knots.Count != next.Count)
            changed = true;
        else
            for (var i = 0; i < next.Count; i++)
                changed |= next[i].Id != spline.Knots[i].Id;
        spline.Knots = next;
        spline.Closed = closed;
        if (changed)
            for (var i = 0; i < next.Count; i++)
                if (next[i].Mode == SplineKnot.Auto)
                    SplineGeometry.Smooth(spline, i, spline.Strength);
    }

    public static string Error(SpatialSpline? spline, IReadOnlyList<SpatialCapture> anchors, bool closed)
    {
        var error = SplineGeometry.Error(spline);
        if (error.Length > 0 || spline == null)
            return error;
        if (spline.Closed != closed)
            return "Spline closure does not match the route end mode.";
        var index = 0;
        foreach (var knot in spline.Knots)
        {
            if (knot.AnchorId.Length == 0)
                continue;
            if (index >= anchors.Count || anchors[index] == null || knot.AnchorId != anchors[index++].Id)
                return "Spline anchors do not match route order.";
        }
        if (index != anchors.Count)
            return "Spline is missing a route anchor.";
        if (spline.Knots.Count > 0 && (spline.Knots[0].AnchorId.Length == 0 || (!closed && spline.Knots[^1].AnchorId.Length == 0)))
            return "An open spline must start and finish at route anchors.";
        return "";
    }

    public static SpatialSpline Resolved(SpatialSpline source, IReadOnlyList<SpatialCapture> anchors)
    {
        var result = Seasons.SeasonCompiler.Copy(source);
        foreach (var knot in result.Knots)
        foreach (var anchor in anchors)
            if (knot.AnchorId == anchor.Id)
            {
                knot.Position = Copy(anchor.Position);
                break;
            }
        return result;
    }

    public static List<SplineSample> Leg(MapPatrolRoute route, int from, int to)
    {
        var error = Error(route.Spline, route.Waypoints, route.Completion == MapPatrolRoute.Loop);
        if (error.Length > 0 || route.Spline == null)
            throw new ArgumentException(error.Length > 0 ? error : "Route has no spline.");
        if (from < 0 || to < 0 || from >= route.Waypoints.Count || to >= route.Waypoints.Count || from == to)
            throw new ArgumentOutOfRangeException(nameof(from));
        var reverse = route.Completion == MapPatrolRoute.PingPong && to < from;
        var first = reverse ? to : from;
        var last = reverse ? from : to;
        if (last != first + 1 && !(route.Completion == MapPatrolRoute.Loop && first == route.Waypoints.Count - 1 && last == 0))
            throw new ArgumentException("Spline legs must connect consecutive route anchors.");
        var source = Resolved(route.Spline, route.Waypoints);
        var begin = source.Knots.FindIndex(k => k.AnchorId == route.Waypoints[first].Id);
        var end = source.Knots.FindIndex(k => k.AnchorId == route.Waypoints[last].Id);
        var leg = new SpatialSpline { Strength = source.Strength };
        for (var i = begin; ; i = (i + 1) % source.Knots.Count)
        {
            leg.Knots.Add(source.Knots[i]);
            if (i == end)
                break;
        }
        if (reverse)
            SplineGeometry.Reverse(leg);
        return SplineGeometry.Sample(leg);
    }
}
