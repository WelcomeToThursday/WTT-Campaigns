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
                $"{From + 1} → {To + 1}: "
                + (
                    Result.Status == EncounterPathStatus.Complete ? $"{Result.Distance:F1} m"
                    : Result.Status == EncounterPathStatus.Pending ? "checking…"
                    : Result.Reason
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
        bool ready = true
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
            Route = route;
            _selection = selection;
            _layoutRevision = layoutRevision;
            _navigationRevision = navigationRevision;
            _cursor = 0;
            _summary = _details = null;
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
            segment.Result =
                from?.Finite == true && to?.Finite == true
                    ? query(from, to)
                    : new(EncounterPathStatus.Failed, reason: "Invalid waypoint coordinates");
            if (!Pending)
                _refreshAt = now + 2;
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
        foreach (var segment in Segments)
        {
            if (segment.Result.Status == EncounterPathStatus.Failed)
                failed++;
            distance += segment.Result.Distance;
        }
        text.Append(
            Pending ? "\nChecking paths…"
            : failed > 0 ? $"\nDraft invalid: {failed} connection(s) blocked"
            : $"\nComplete route · {distance:F1} m"
        );
        text.Append("\nArrows: walkable path · Dashed: pending or failed connection");
        foreach (var segment in Segments)
            if (details || segment.Result.Status == EncounterPathStatus.Failed)
                text.Append('\n').Append(segment.Caption);
        return text.ToString();
    }
}
