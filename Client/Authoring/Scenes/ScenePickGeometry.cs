using System;
using System.Numerics;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class ScenePickGeometry
{
    // Direction is deliberately not normalized after transforming to mesh space:
    // its parameter remains a world-space distance, even with nonuniform scale.
    internal static bool Triangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, ref float nearest)
    {
        var ab = b - a;
        var ac = c - a;
        var p = Vector3.Cross(direction, ac);
        var determinant = Vector3.Dot(ab, p);
        if (Math.Abs(determinant) < 1e-8f)
            return false;
        var inverse = 1f / determinant;
        var offset = origin - a;
        var u = Vector3.Dot(offset, p) * inverse;
        if (u < 0 || u > 1)
            return false;
        var q = Vector3.Cross(offset, ab);
        var v = Vector3.Dot(direction, q) * inverse;
        if (v < 0 || u + v > 1)
            return false;
        var distance = Vector3.Dot(ac, q) * inverse;
        if (distance < 0 || distance >= nearest)
            return false;
        nearest = distance;
        return true;
    }
}
