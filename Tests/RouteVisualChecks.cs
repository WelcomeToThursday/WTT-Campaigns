using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class RouteVisualChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var points = new List<(SpatialCapture Point, RouteRole Role, int Number)>();
        var layout = new MapLayout();
        RouteVisuals.Points(layout, points);
        check(points.Count == 0, "Empty routes have no invented markers");
        layout.Start = new() { Id = "start" };
        RouteVisuals.Points(layout, points);
        check(points.Count == 1 && points[0].Role == RouteRole.Start, "A start-only route has a visible start marker");
        layout.Checkpoints.Add(new() { Id = "b" });
        layout.Checkpoints.Add(new() { Id = "a" });
        layout.Exit = new() { Id = "end" };
        RouteVisuals.Points(layout, points);
        check(points.Select(p => p.Point.Id).SequenceEqual(new[] { "start", "b", "a", "end" }), "Visual traversal follows authored order rather than sorting IDs");
        check(points[1].Number == 1 && points[2].Number == 2 && points[3].Role == RouteRole.End, "Checkpoint numbers and end role match traversal");
        check(RouteVisuals.Label(points[0].Role, 0) == "START" && RouteVisuals.Label(points[1].Role, 1) == "CHECKPOINT 1" && RouteVisuals.Label(points[3].Role, 0) == "END", "Roles remain identifiable without color");
        check(new[] { RouteVisuals.Color(RouteRole.Start), RouteVisuals.Color(RouteRole.Checkpoint), RouteVisuals.Color(RouteRole.End) }.Distinct().Count() == 3, "All waypoint role colors are distinct");
        layout.Checkpoints.Reverse();
        layout.Start = null;
        layout.Exit = null;
        RouteVisuals.Points(layout, points);
        check(points.Count == 2 && points[0].Point.Id == "a" && points[0].Number == 1, "Reordering and missing endpoints replace pooled visual data without stale markers");
        layout.Checkpoints.Clear();
        layout.Exit = new() { Id = "end" };
        RouteVisuals.Points(layout, points);
        check(points.Count == 1 && points[0].Role == RouteRole.End, "Exit-only incomplete routes retain their marker");

        check(RouteVisuals.ClipNear(1, 10, .1f, out var start, out var end) && start == 0 && end == 1, "Fully visible connections retain both endpoints");
        check(!RouteVisuals.ClipNear(-10, -.5f, .1f, out _, out _), "Connections behind the camera are hidden");
        check(RouteVisuals.ClipNear(-1, 1, .1f, out start, out end) && Math.Abs(start - .55f) < .00001f && end == 1, "Crossing connections clip before perspective projection");
        check(RouteVisuals.ClipNear(1, -1, .1f, out start, out end) && start == 0 && Math.Abs(end - .45f) < .00001f, "Reverse crossing connections clip the second endpoint");
        check(RouteVisuals.ClipNear(.1f, .1f, .1f, out start, out end) && start == 0 && end == 1, "Near-plane aligned connections do not divide by zero");
        check(!RouteVisuals.ClipNear(float.NaN, 1, .1f, out _, out _) && !RouteVisuals.ClipNear(1, float.PositiveInfinity, .1f, out _, out _), "Invalid depths cannot enter the canvas mesh");
        foreach (var (width, height) in new[] { (1280f, 720f), (1920f, 1080f), (3440f, 1440f) })
        {
            float ax = -1000000, ay = height / 2, bx = 1000000, by = height / 2;
            check(RouteVisuals.ClipScreen(ref ax, ref ay, ref bx, ref by, width, height) && Math.Abs(ax) < .01 && Math.Abs(bx - width) < .01 && ay == by, "Long projected connection clips to viewport at " + width);
            ax = -100; ay = -50; bx = width; by = -50;
            check(!RouteVisuals.ClipScreen(ref ax, ref ay, ref bx, ref by, width, height), "Off-screen parallel connection is omitted at " + width);
            ax = width / 2; ay = -5000; bx = width / 2; by = 5000;
            check(RouteVisuals.ClipScreen(ref ax, ref ay, ref bx, ref by, width, height) && ay == 0 && by == height, "Vertical connection clips without a horizontal denominator at " + width);
        }
        float x = float.NaN, y = 0, x2 = 10, y2 = 10;
        check(!RouteVisuals.ClipScreen(ref x, ref y, ref x2, ref y2, 1920, 1080), "Invalid projected coordinates are rejected");
    }
}
