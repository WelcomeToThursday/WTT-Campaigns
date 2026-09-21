using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal static class NavigationSurfaceGeometry
{
    // Keep triangles crossing the height band, including ramps whose centers fall outside it.
    internal static int[] Filter(Vector3[] vertices, int[] indices, float? floor)
    {
        if (!floor.HasValue)
            return indices;
        var selected = new List<int>();
        var low = floor.Value - .6f;
        var high = floor.Value + .6f;
        for (var i = 0; i < indices.Length; i += 3)
        {
            var a = vertices[indices[i]].y;
            var b = vertices[indices[i + 1]].y;
            var c = vertices[indices[i + 2]].y;
            if (Math.Max(a, Math.Max(b, c)) < low || Math.Min(a, Math.Min(b, c)) > high)
                continue;
            selected.Add(indices[i]);
            selected.Add(indices[i + 1]);
            selected.Add(indices[i + 2]);
        }
        return selected.ToArray();
    }
}
