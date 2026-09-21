using System.Numerics;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Pure polygon clipping, shared with offline checks. Source heights are interpolated,
// never projected onto guessed floor planes. Subtraction preserves holes and islands.
internal static class NavigationPaintGeometry
{
    internal static List<Vector3> Clip(List<Vector3> polygon, Vector3 normal, float distance)
    {
        var result = new List<Vector3>();
        if (polygon.Count == 0)
            return result;
        var previous = polygon[polygon.Count - 1];
        var before = Vector3.Dot(previous, normal) - distance;
        foreach (var point in polygon)
        {
            var after = Vector3.Dot(point, normal) - distance;
            if ((before >= 0) != (after >= 0))
                result.Add(Vector3.Lerp(previous, point, before / (before - after)));
            if (after >= 0)
                result.Add(point);
            previous = point;
            before = after;
        }
        return result;
    }

    internal static List<Vector3> Box(Vector3 a, Vector3 b, Vector3 c, Vector3 min, Vector3 max)
    {
        var p = new List<Vector3> { a, b, c };
        p = Clip(p, Vector3.UnitX, min.X);
        p = Clip(p, -Vector3.UnitX, -max.X);
        p = Clip(p, Vector3.UnitY, min.Y);
        p = Clip(p, -Vector3.UnitY, -max.Y);
        p = Clip(p, Vector3.UnitZ, min.Z);
        return Clip(p, -Vector3.UnitZ, -max.Z);
    }

    internal static List<List<Vector3>> Subtract(List<Vector3> polygon, Vector3 a, Vector3 b, Vector3 c)
    {
        var output = new List<List<Vector3>>();
        var points = new[] { a, b, c };
        var cross = (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
        if (Math.Abs(cross) < .000001f)
            return new() { polygon };
        for (var i = 0; i < 3 && polygon.Count >= 3; i++)
        {
            var from = points[i];
            var edge = points[(i + 1) % 3] - from;
            var inward = new Vector3(-edge.Z, 0, edge.X) * Math.Sign(cross);
            var distance = Vector3.Dot(inward, from);
            var outside = Clip(polygon, -inward, -distance);
            if (outside.Count >= 3)
                output.Add(outside);
            polygon = Clip(polygon, inward, distance);
        }
        return output;
    }

    internal static List<List<Vector3>> SubtractBox(List<Vector3> polygon, Vector3 min, Vector3 max)
    {
        var output = new List<List<Vector3>>();
        var planes = new[]
        {
            (Vector3.UnitX, min.X),
            (-Vector3.UnitX, -max.X),
            (Vector3.UnitY, min.Y),
            (-Vector3.UnitY, -max.Y),
            (Vector3.UnitZ, min.Z),
            (-Vector3.UnitZ, -max.Z),
        };
        foreach (var plane in planes)
        {
            var outside = Clip(polygon, -plane.Item1, -plane.Item2);
            if (outside.Count >= 3)
                output.Add(outside);
            polygon = Clip(polygon, plane.Item1, plane.Item2);
            if (polygon.Count < 3)
                break;
        }
        return output;
    }

    internal static float Area(List<Vector3> polygon)
    {
        var area = 0f;
        for (var i = 1; i + 1 < polygon.Count; i++)
            area += Vector3.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]).Length() * .5f;
        return area;
    }
}
