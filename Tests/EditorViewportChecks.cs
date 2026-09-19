using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class EditorViewportChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var pixels = EditorViewportCoordinates.Pixels(new(200, 100, 800, 600), 1, 1920, 1080);
        check(
            pixels.X == 200 && pixels.Y == 380 && pixels.Width == 800 && pixels.Height == 600,
            "Viewport converts the top-left dock origin to bottom-left screen pixels."
        );
        check(
            EditorViewportCoordinates.Normalize(pixels, 600, 680) == (.5f, .5f),
            "The center of an offset viewport casts through the camera center."
        );
        check(
            EditorViewportCoordinates.Project(pixels, 0, 0) == (200f, 380f)
                && EditorViewportCoordinates.Project(pixels, 1, 1) == (1000f, 980f),
            "World projection reaches the viewport corners, not the monitor corners."
        );
        check(
            EditorViewportCoordinates.Normalize(pixels, 100, 300).X < 0,
            "Captured drags outside the viewport retain their unclamped coordinates."
        );

        foreach (var scale in new[] { .75f, 1f, 1.25f, 1.5f })
        foreach (var size in new[] { (1280, 720), (1920, 1080), (3440, 1440) })
        foreach (var inspector in new[] { false, true })
        {
            var root = EditorDockNode.Default();
            var visible = new HashSet<string> { "Tool:Layouts" };
            if (inspector)
                visible.Add("Inspector");
            var area = new EditorDockRect(56, 84, size.Item1 / scale - 64, size.Item2 / scale - 124);
            root = EditorDockLayout.FitDisplay(root, area, visible);
            var dock = EditorDockLayout.Arrange(root, area, visible)[EditorDockLayout.Nodes(root).Single(n => n.Kind == "viewport").Id];
            var rect = EditorViewportCoordinates.Pixels(dock, scale, size.Item1, size.Item2);
            check(
                rect.Width > 0
                    && rect.Height > 0
                    && rect.X >= 0
                    && rect.Y >= 0
                    && rect.X + rect.Width <= size.Item1
                    && rect.Y + rect.Height <= size.Item2,
                "Scaled and fitted dock layouts always provide a bounded render surface."
            );
            foreach (var point in new[] { (0f, 0f), (.5f, .5f), (1f, 1f), (-.2f, 1.3f) })
            {
                var screen = EditorViewportCoordinates.Project(rect, point.Item1, point.Item2);
                var roundTrip = EditorViewportCoordinates.Normalize(rect, screen.X, screen.Y);
                check(
                    Math.Abs(roundTrip.X - point.Item1) < .00001f && Math.Abs(roundTrip.Y - point.Item2) < .00001f,
                    "Viewport projection and pointer normalization remain inverse after resizing and UI scaling."
                );
            }
        }
        var tiny = EditorViewportCoordinates.Pixels(new(-5, -5, 0, 0), 1, 1, 1);
        check(tiny.Width == 1 && tiny.Height == 1 && tiny.X == 0 && tiny.Y == 0, "Minimized windows never yield a zero-sized viewport.");
    }
}
