using System.Numerics;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Engine-free geometry used by both the tree audit and its offline regression checks.
internal static class NavigationTreeGeometry
{
    internal static Matrix4x4 Placement(
        Vector3 terrainOrigin,
        Vector3 terrainSize,
        Vector3 position,
        float rotation,
        float width,
        float height
    )
    {
        if (
            !Finite(terrainOrigin)
            || !Finite(terrainSize)
            || !Finite(position)
            || position.X < 0
            || position.Y < 0
            || position.Z < 0
            || position.X > 1
            || position.Y > 1
            || position.Z > 1
            || !Finite(rotation)
            || !Finite(width)
            || !Finite(height)
            || width <= 0
            || height <= 0
        )
            throw new InvalidOperationException("Invalid terrain tree position, rotation or scale.");
        return Matrix4x4.CreateScale(width, height, width)
            * Matrix4x4.CreateRotationY(rotation)
            * Matrix4x4.CreateTranslation(terrainOrigin + terrainSize * position);
    }

    internal static Vector3[] Corners(Matrix4x4 transform, Vector3 center, Vector3 size)
    {
        if (!Finite(center) || !Finite(size) || size.X < 0 || size.Y < 0 || size.Z < 0)
            throw new InvalidOperationException("Invalid tree collider bounds.");
        var points = new Vector3[8];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = Vector3.Transform(
                center + size * new Vector3((i & 1) == 0 ? -.5f : .5f, (i & 2) == 0 ? -.5f : .5f, (i & 4) == 0 ? -.5f : .5f),
                transform
            );
            if (!Finite(points[i]))
                throw new InvalidOperationException("Tree collider transform is not finite.");
        }
        return points;
    }

    internal static bool Intersects(Vector3[] points, Vector3 center, Vector3 size)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return min.X <= center.X + size.X / 2
            && max.X >= center.X - size.X / 2
            && min.Y <= center.Y + size.Y / 2
            && max.Y >= center.Y - size.Y / 2
            && min.Z <= center.Z + size.Z / 2
            && max.Z >= center.Z - size.Z / 2;
    }

    // Broad phase only: this envelope never proves collision coverage. It encloses
    // either root-transform convention and arbitrary prototype/instance rotation.
    // Invalid or unknown bounds stay in the audit instead of being silently skipped.
    internal static bool OutsideEnvelope(
        Matrix4x4 relative,
        Vector3 rootScale,
        Vector3 rootPosition,
        Matrix4x4 placement,
        Vector3 colliderCenter,
        Vector3 colliderSize,
        Vector3 probeCenter,
        Vector3 probeSize
    )
    {
        if (
            !Finite(rootScale)
            || !Finite(rootPosition)
            || !Finite(colliderCenter)
            || !Finite(colliderSize)
            || !Finite(probeCenter)
            || !Finite(probeSize)
            || colliderSize.X < 0
            || colliderSize.Y < 0
            || colliderSize.Z < 0
            || probeSize.X < 0
            || probeSize.Y < 0
            || probeSize.Z < 0
        )
            return false;
        var rx = new Vector3(relative.M11, relative.M12, relative.M13);
        var ry = new Vector3(relative.M21, relative.M22, relative.M23);
        var rz = new Vector3(relative.M31, relative.M32, relative.M33);
        var px = new Vector3(placement.M11, placement.M12, placement.M13);
        var py = new Vector3(placement.M21, placement.M22, placement.M23);
        var pz = new Vector3(placement.M31, placement.M32, placement.M33);
        if (
            !Finite(rx)
            || !Finite(ry)
            || !Finite(rz)
            || !Finite(px)
            || !Finite(py)
            || !Finite(pz)
            || !Finite(relative.Translation)
            || !Finite(placement.Translation)
            || rx.LengthSquared() <= 0
            || ry.LengthSquared() <= 0
            || rz.LengthSquared() <= 0
        )
            return false;
        var relativeStretch = StretchBound(rx, ry, rz);
        var placementStretch = StretchBound(px, py, pz);
        var rootStretch = Math.Max(1, Math.Max(Math.Abs(rootScale.X), Math.Max(Math.Abs(rootScale.Y), Math.Abs(rootScale.Z))));
        var localRadius = Vector3.Transform(colliderCenter, relative).Length() + relativeStretch * colliderSize.Length() / 2;
        var radius = (localRadius * rootStretch + rootPosition.Length()) * placementStretch + .001f;
        if (!Finite(radius) || placementStretch <= 0)
            return false;
        var nearest = Vector3.Min(Vector3.Max(placement.Translation, probeCenter - probeSize / 2), probeCenter + probeSize / 2);
        return Vector3.DistanceSquared(placement.Translation, nearest) > radius * radius;
    }

    // The largest absolute row sum of A*A^T bounds its largest eigenvalue.
    // Unlike the Frobenius norm, this is 1 for a rotation rather than sqrt(3):
    // composing two ordinary transforms no longer triples the envelope radius.
    // Nonuniform scales, mirrors and shears still have a conservative bound.
    internal static float StretchBound(Vector3 x, Vector3 y, Vector3 z)
    {
        double Dot(Vector3 a, Vector3 b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
        var xy = Math.Abs(Dot(x, y));
        var xz = Math.Abs(Dot(x, z));
        var yz = Math.Abs(Dot(y, z));
        return (float)(Math.Sqrt(Math.Max(Dot(x, x) + xy + xz, Math.Max(Dot(y, y) + xy + yz, Dot(z, z) + xz + yz))) * 1.000001);
    }

    // Ordered corner comparison deliberately rejects mirroring/reorientation of a mesh,
    // even when its axis-aligned world bounds happen to be identical.
    internal static bool Matches(Vector3[] expected, Vector3[] actual)
    {
        if (expected.Length != actual.Length || expected.Length == 0)
            return false;
        for (var i = 0; i < expected.Length; i++)
            if (!Finite(expected[i]) || !Finite(actual[i]) || Vector3.DistanceSquared(expected[i], actual[i]) > .000001f)
                return false;
        return true;
    }

    internal static bool MatchesBox(Vector3[] expected, Vector3[] actual)
    {
        if (expected.Length != 8 || actual.Length != 8)
            return false;
        var used = new bool[8];
        foreach (var point in expected)
        {
            var found = false;
            for (var i = 0; i < actual.Length; i++)
                if (!used[i] && Close(point, actual[i]))
                {
                    used[i] = found = true;
                    break;
                }
            if (!found)
                return false;
        }
        return true;
    }

    internal static bool MatchesRound(Vector3[] expected, Vector3[] actual, bool capsule)
    {
        if (
            !Round(expected, capsule, out var a, out var b, out var radius)
            || !Round(actual, capsule, out var c, out var d, out var otherRadius)
            || Math.Abs(radius - otherRadius) > .001f
        )
            return false;
        return Close(a, c) && Close(b, d) || Close(a, d) && Close(b, c);
    }

    private static bool Round(Vector3[] corners, bool capsule, out Vector3 a, out Vector3 b, out float radius)
    {
        a = b = default;
        radius = 0;
        if (corners.Length != 8)
            return false;
        foreach (var corner in corners)
            if (!Finite(corner))
                return false;
        var x = corners[1] - corners[0];
        var y = corners[2] - corners[0];
        var z = corners[4] - corners[0];
        var dx = x.Length();
        var dy = y.Length();
        var dz = z.Length();
        if (
            !Finite(dx)
            || !Finite(dy)
            || !Finite(dz)
            || dx < .00001f
            || dy < .00001f
            || dz < .00001f
            || Math.Abs(dx - dz) > .001f
            || (!capsule && Math.Abs(dx - dy) > .001f)
            || (capsule && dy < dx - .001f)
        )
            return false;
        x /= dx;
        y /= dy;
        z /= dz;
        if (Math.Abs(Vector3.Dot(x, y)) > .0001f || Math.Abs(Vector3.Dot(x, z)) > .0001f || Math.Abs(Vector3.Dot(y, z)) > .0001f)
            return false; // Do not infer primitive physics from a sheared transform.
        radius = dx / 2;
        var center = (corners[0] + corners[7]) / 2;
        var halfSegment = capsule ? Math.Max(0, dy / 2 - radius) : 0;
        a = center - y * halfSegment;
        b = center + y * halfSegment;
        return true;
    }

    private static bool Close(Vector3 a, Vector3 b) => Finite(a) && Finite(b) && Vector3.DistanceSquared(a, b) <= .000001f;

    private static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
