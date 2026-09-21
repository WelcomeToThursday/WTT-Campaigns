using System.Numerics;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Identical coverage calculation for runtime rejection and offline replay.
internal sealed class NavigationPaintFootprint
{
    private readonly Dictionary<(int, int), List<MapNavigationCell>> _cells = new();

    internal NavigationPaintFootprint(MapNavigationRecipe recipe)
    {
        foreach (var cell in recipe.Cells)
        {
            if (cell.Mode != "Add")
                continue;
            if (!_cells.TryGetValue((cell.X, cell.Z), out var floors))
                _cells[(cell.X, cell.Z)] = floors = new();
            floors.Add(cell);
        }
    }

    internal List<List<Vector3>> Outside(Vector3 a, Vector3 b, Vector3 c)
    {
        var remaining = new List<List<Vector3>>
        {
            new() { a, b, c },
        };
        var min = Vector3.Min(a, Vector3.Min(b, c));
        var max = Vector3.Max(a, Vector3.Max(b, c));
        for (var x = (int)Math.Floor(min.X) - 1; x <= (int)Math.Floor(max.X); x++)
        for (var z = (int)Math.Floor(min.Z) - 1; z <= (int)Math.Floor(max.Z); z++)
            if (_cells.TryGetValue((x, z), out var floors))
                foreach (var cell in floors)
                {
                    var next = new List<List<Vector3>>();
                    foreach (var piece in remaining)
                        next.AddRange(
                            NavigationPaintGeometry.SubtractBox(
                                piece,
                                new(cell.X - .001f, cell.Y - MapNavigationPainting.FloorTolerance - .001f, cell.Z - .001f),
                                new(cell.X + 1.001f, cell.Y + MapNavigationPainting.FloorTolerance + .001f, cell.Z + 1.001f)
                            )
                        );
                    remaining = next;
                    if (remaining.Count == 0)
                        return remaining;
                }
        return remaining;
    }
}
