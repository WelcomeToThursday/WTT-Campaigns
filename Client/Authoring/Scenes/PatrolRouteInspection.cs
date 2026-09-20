using System.Text;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Queries are independent of camera projection and spread across frames.
internal sealed class PatrolRouteInspection
{
    internal sealed class Segment(int from, int to)
    {
        internal readonly int From = from,
            To = to;
        private EncounterPathResult _result = new(EncounterPathStatus.Pending);
        private string? _caption;
        internal EncounterPathResult Result
        {
            get => _result;
            set
            {
                _result = value;
                _caption = null;
            }
        }
        internal string Caption =>
            _caption ??=
                $"Waypoint {From + 1} → Waypoint {To + 1}"
                + (
                    Result.Status == EncounterPathStatus.Complete ? $"\nPath length: {Result.Distance:F1} m"
                    : Result.Status == EncounterPathStatus.Pending ? ""
                    : ": " + Result.Reason
                );
    }

    internal readonly List<Segment> Segments = new();
    internal MapPatrolRoute? Route { get; private set; }
    private long _layoutRevision,
        _navigationRevision;
    private string _selection = "";
    private double _refreshAt;
    private int _cursor,
        _frame = -1,
        _queries;
    private string? _summary,
        _details;
    internal bool Pending => _cursor < Segments.Count;

    internal void Refresh(
        MapPatrolRoute? route,
        string selection,
        long layoutRevision,
        long navigationRevision,
        int frame,
        double now,
        Func<SpatialVector, SpatialVector, EncounterPathResult> query,
        bool ready = true,
        Func<MapPatrolRoute, int, int, EncounterPathResult>? curveQuery = null
    )
    {
        if (frame != _frame)
        {
            _frame = frame;
            _queries = 0;
        }
        if (
            !ReferenceEquals(Route, route)
            || _selection != selection
            || _layoutRevision != layoutRevision
            || _navigationRevision != navigationRevision
            || (!Pending && now >= _refreshAt)
        )
        {
            // Refresh diagnostics in place. Background validation must not replace a
            // settled route with pending geometry/status on every two-second pass.
            var geometryChanged = !ReferenceEquals(Route, route) || _layoutRevision != layoutRevision;
            Route = route;
            _selection = selection;
            _layoutRevision = layoutRevision;
            _navigationRevision = navigationRevision;
            _cursor = 0;
            _summary = _details = null;
            if (geometryChanged)
            {
                Segments.Clear();
                if (route?.Waypoints != null)
                {
                    for (var i = 1; i < route.Waypoints.Count; i++)
                    {
                        Segments.Add(new(i - 1, i));
                        if (route.Completion == MapPatrolRoute.PingPong)
                            Segments.Add(new(i, i - 1));
                    }
                    if (route.Completion == MapPatrolRoute.Loop && route.Waypoints.Count > 1)
                        Segments.Add(new(route.Waypoints.Count - 1, 0));
                    if (route.Spline != null)
                        PreviewCurve(route);
                }
            }
            _refreshAt = now + 2;
        }
        while (ready && Pending && _queries < 4)
        {
            _summary = _details = null;
            var segment = Segments[_cursor++];
            _queries++;
            var from = route!.Waypoints[segment.From]?.Position;
            var to = route.Waypoints[segment.To]?.Position;
            var result =
                from?.Finite == true && to?.Finite == true
                    ? route.Spline != null && curveQuery != null
                        ? curveQuery(route, segment.From, segment.To)
                        : query(from, to)
                    : new(EncounterPathStatus.Failed, reason: "Invalid waypoint coordinates");
            if (result.Status == EncounterPathStatus.Pending)
            {
                if (segment.Result.Status == EncounterPathStatus.Pending && result.Corners.Length > 1)
                    segment.Result = result;
                _cursor--;
                break;
            }
            segment.Result = result;
            if (!Pending)
                _refreshAt = now + 2;
        }
    }

    private void PreviewCurve(MapPatrolRoute route)
    {
        // Draw every leg immediately, even while the first leg is waiting for navigation.
        // Sample the whole curve once; adjacent legs share their exact anchor sample.
        try
        {
            var error = RouteSpline.Error(route.Spline, route.Waypoints, route.Completion == MapPatrolRoute.Loop);
            if (error.Length > 0)
                throw new ArgumentException(error);
            var spline = RouteSpline.Resolved(route.Spline!, route.Waypoints);
            var samples = SplineGeometry.Sample(spline);
            var anchors = new Dictionary<string, int>(StringComparer.Ordinal);
            if (samples.Count == 0)
                return;
            anchors[spline.Knots[0].AnchorId] = 0;
            for (var i = 1; i < samples.Count; i++)
            {
                var knot = samples[i].Segment + 1;
                if (samples[i].T == 1 && knot < spline.Knots.Count && spline.Knots[knot].AnchorId.Length > 0)
                    anchors[spline.Knots[knot].AnchorId] = i;
            }
            foreach (var segment in Segments)
            {
                var from = anchors[route.Waypoints[segment.From].Id];
                var to =
                    route.Completion == MapPatrolRoute.Loop && segment.To == 0
                        ? samples.Count - 1
                        : anchors[route.Waypoints[segment.To].Id];
                var points = new SpatialVector[Math.Abs(to - from) + 1];
                var direction = to >= from ? 1 : -1;
                for (var i = 0; i < points.Length; i++)
                    points[i] = SplineGeometry.Spatial(samples[from + i * direction].Position);
                segment.Result = new(EncounterPathStatus.Pending, points);
            }
        }
        catch (ArgumentException e)
        {
            foreach (var segment in Segments)
                segment.Result = new(EncounterPathStatus.Failed, reason: e.Message);
        }
    }

    internal string Summary(bool details) => details ? _details ??= BuildSummary(true) : _summary ??= BuildSummary(false);

    private string BuildSummary(bool details)
    {
        if (Route == null)
            return "";
        var text = new StringBuilder("PATROL · ").Append(Route.Name);
        if (Route.Waypoints.Count < 2)
            return text.Append("\nDraft incomplete: add at least two waypoints before playtesting.").ToString();
        var failed = 0;
        var distance = 0f;
        var unsettled = false;
        foreach (var segment in Segments)
        {
            if (segment.Result.Status == EncounterPathStatus.Failed)
                failed++;
            distance += segment.Result.Distance;
            unsettled |= segment.Result.Status == EncounterPathStatus.Pending;
        }
        text.Append(
            failed > 0 ? $"\nDraft invalid: {failed} connection(s) blocked"
            : unsettled ? ""
            : $"\nComplete route · {distance:F1} m"
        );
        text.Append(
            "\nArrows: walkable path · Red: blocked connection\nPurple: off NavMesh · Orange: standing clearance\nBright cross: first problem · Fade: nearby curve · Grey: unconfirmed"
        );
        foreach (var segment in Segments)
            if (details || segment.Result.Status == EncounterPathStatus.Failed)
                text.Append('\n').Append(segment.Caption);
        return text.ToString();
    }
}
