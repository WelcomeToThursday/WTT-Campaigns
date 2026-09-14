using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

internal enum RouteRole
{
    Start,
    Checkpoint,
    End,
    Patrol,
    Spawn,
    Trigger,
}

internal static class RouteVisuals
{
    internal const int StartColor = 0x66BB6A,
        CheckpointColor = 0xFFCA28,
        EndColor = 0xEF5350;

    internal static void Points(MapLayout layout, List<(SpatialCapture Point, RouteRole Role, int Number)> result)
    {
        result.Clear();
        if (layout.Start != null)
            result.Add((layout.Start, RouteRole.Start, 0));
        for (var i = 0; i < layout.Checkpoints.Count; i++)
            result.Add((layout.Checkpoints[i], RouteRole.Checkpoint, i + 1));
        if (layout.Exit != null)
            result.Add((layout.Exit, RouteRole.End, 0));
    }

    internal static int Color(RouteRole role) =>
        role == RouteRole.Start ? StartColor
        : role == RouteRole.End ? EndColor
        : role == RouteRole.Patrol ? 0x42A5F5
        : role == RouteRole.Spawn ? 0xAB47BC
        : role == RouteRole.Trigger ? 0x26A69A
        : CheckpointColor;

    internal static string Label(RouteRole role, int number) =>
        role == RouteRole.Start ? "START"
        : role == RouteRole.End ? "END"
        : role == RouteRole.Patrol ? "PATROL " + number
        : role == RouteRole.Spawn ? "BOT SPAWN"
        : role == RouteRole.Trigger ? "TRIGGER"
        : "CHECKPOINT " + number;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    // Clip in depth before perspective projection, including segments crossing the camera.
    internal static bool ClipNear(float a, float b, float near, out float from, out float to)
    {
        from = 0;
        to = 1;
        if (!Finite(a) || !Finite(b) || !Finite(near) || near <= 0 || (a < near && b < near))
            return false;
        if (a < near)
            from = (float)(((double)near - a) / ((double)b - a));
        if (b < near)
            to = (float)(((double)near - a) / ((double)b - a));
        return true;
    }

    internal static bool ClipScreen(ref float ax, ref float ay, ref float bx, ref float by, float width, float height)
    {
        if (!Finite(ax) || !Finite(ay) || !Finite(bx) || !Finite(by) || !Finite(width) || !Finite(height) || width <= 0 || height <= 0)
            return false;
        // Double intermediates also keep very distant finite endpoints safe.
        double dx = (double)bx - ax,
            dy = (double)by - ay,
            start = 0,
            end = 1;
        bool Edge(double p, double q)
        {
            if (p == 0)
                return q >= 0;
            var t = q / p;
            if (p < 0)
                start = Math.Max(start, t);
            else
                end = Math.Min(end, t);
            return start <= end;
        }
        if (!Edge(-dx, ax) || !Edge(dx, width - (double)ax) || !Edge(-dy, ay) || !Edge(dy, height - (double)ay))
            return false;
        bx = (float)(ax + end * dx);
        by = (float)(ay + end * dy);
        ax = (float)(ax + start * dx);
        ay = (float)(ay + start * dy);
        return true;
    }
}
