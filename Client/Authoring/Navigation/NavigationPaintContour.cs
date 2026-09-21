using System.Numerics;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Display geometry only: never move X/Z, bridge missing samples, or select another floor.
internal static class NavigationPaintContour
{
    internal const int Divisions = 4;

    internal static (Vector3[] Vertices, int[] Indices) Build(int x, int z, float height, Func<Vector3, Vector3?> sample)
    {
        var vertices = new Vector3[25];
        var valid = new bool[25];
        for (var row = 0; row <= Divisions; row++)
        for (var column = 0; column <= Divisions; column++)
        {
            var i = row * 5 + column;
            var requested = new Vector3(x + column / 4f, height, z + row / 4f);
            var hit = sample(requested);
            if (!hit.HasValue || (float.IsNaN(hit.Value.Y) || float.IsInfinity(hit.Value.Y)) || Math.Abs(hit.Value.Y - height) > .6001f)
                continue;
            vertices[i] = new Vector3(requested.X, hit.Value.Y, requested.Z);
            valid[i] = true;
        }
        var indices = new List<int>(96);
        void Triangle(int a, int b, int c)
        {
            if (!valid[a] || !valid[b] || !valid[c])
                return;
            // Reject abrupt ledges rather than draping a false ramp over them.
            foreach (var edge in new[] { (a, b), (b, c), (c, a) })
            {
                var delta = vertices[edge.Item1] - vertices[edge.Item2];
                if (delta.Y * delta.Y > (delta.X * delta.X + delta.Z * delta.Z) * 3 + .0025f)
                    return;
            }
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
        for (var row = 0; row < Divisions; row++)
        for (var column = 0; column < Divisions; column++)
        {
            var a = row * 5 + column;
            Triangle(a, a + 5, a + 6);
            Triangle(a, a + 6, a + 1);
        }
        return (vertices, indices.ToArray());
    }
}
