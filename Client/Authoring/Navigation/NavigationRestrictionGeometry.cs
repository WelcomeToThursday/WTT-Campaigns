using System.Numerics;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal static class NavigationRestrictionGeometry
{
    // Excludes standing positions whose body could intersect the physical AABB.
    // This is deliberately conservative: it adds no walkable support or guessed hull.
    internal static (Vector3 Center, Vector3 Size) ConvexExclusion(
        Vector3 center,
        Vector3 size,
        float radius,
        float height,
        float step,
        float voxel
    )
    {
        if (
            !Finite(center)
            || !Finite(size)
            || size.X <= 0
            || size.Y <= 0
            || size.Z <= 0
            || !Finite(new Vector3(radius, height, step))
            || !Finite(new Vector3(voxel))
            || radius <= 0
            || height <= 0
            || step < 0
            || voxel <= 0
        )
            throw new InvalidOperationException("Convex exclusion has invalid physical bounds or infantry settings");
        var horizontal = radius + 2 * voxel;
        var resultCenter = center - new Vector3(0, height / 2, 0);
        var resultSize = size + new Vector3(2 * horizontal, height + 2 * step + 4 * voxel, 2 * horizontal);
        if (!Finite(resultCenter) || !Finite(resultSize))
            throw new InvalidOperationException("Convex exclusion bounds overflow");
        return (resultCenter, resultSize);
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.X)
        && !float.IsInfinity(value.X)
        && !float.IsNaN(value.Y)
        && !float.IsInfinity(value.Y)
        && !float.IsNaN(value.Z)
        && !float.IsInfinity(value.Z);
}
