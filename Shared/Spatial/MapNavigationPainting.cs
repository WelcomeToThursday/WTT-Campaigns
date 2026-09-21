namespace WTT.Campaigns.Shared.Spatial;

// A one-metre grid records deliberate footprints, not seeds for a flood fill.
// Y belongs to the physical surface selected by the author, independently per floor.
public sealed class MapNavigationCell
{
    public int X { get; set; }
    public int Z { get; set; }
    public float Y { get; set; }
    public string Mode { get; set; } = "Add";
}

public sealed class MapNavigationConnection
{
    public SpatialVector Start { get; set; } = new();
    public SpatialVector End { get; set; } = new();
}

public static class MapNavigationPainting
{
    public const int Limit = 4096;
    public const float FloorTolerance = .6f;

    public static List<string> Errors(MapNavigationRecipe recipe)
    {
        var errors = new List<string>();
        if (recipe.Cells == null || recipe.Connections == null)
        {
            errors.Add("Navigation paint collections cannot be null.");
            return errors;
        }
        if ((recipe.Cells.Count > 0 || recipe.Connections.Count > 0) && recipe.Version != 2)
            errors.Add("Manual navigation edits require recipe version 2.");
        if (recipe.Version == 2 && recipe.Settings != null)
            errors.Add("Manual navigation inherits the native infantry settings.");
        if (recipe.Cells.Count > Limit || recipe.Connections.Count > 128)
            errors.Add("Navigation paint exceeds 4096 cells or 128 connections.");
        var floors = new Dictionary<(int, int), List<float>>();
        foreach (var cell in recipe.Cells)
        {
            if (
                cell == null
                || cell.Mode is not ("Add" or "Block")
                || !Finite(cell.Y)
                || Math.Abs((long)cell.X) > 100000
                || Math.Abs((long)cell.Z) > 100000
            )
            {
                errors.Add("Navigation paint contains an invalid cell.");
                continue;
            }
            var key = (cell.X, cell.Z);
            if (!floors.TryGetValue(key, out var heights))
                floors[key] = heights = new();
            if (heights.Exists(y => Math.Abs(y - cell.Y) <= FloorTolerance))
                errors.Add("Navigation paint contains overlapping edits on the same floor.");
            heights.Add(cell.Y);
        }
        foreach (var link in recipe.Connections)
            if (
                link == null
                || !Point(link.Start)
                || !Point(link.End)
                || DistanceSquared(link.Start, link.End) > 25
                || DistanceSquared(link.Start, link.End) < .01f
            )
                errors.Add("Navigation connections require two finite points between 0.1 and 5 metres apart.");
        return errors;
    }

    public static void Stamp(MapNavigationRecipe recipe, int x, int z, float y, string mode)
    {
        if (mode is not ("Add" or "Block" or "Erase") || !Finite(y))
            throw new ArgumentException("Invalid navigation brush.");
        var existing = recipe.Cells.Find(c => c.X == x && c.Z == z && Math.Abs(c.Y - y) <= FloorTolerance);
        if (mode != "Erase" && existing == null && recipe.Cells.Count >= Limit)
            throw new InvalidOperationException("Navigation paint limit reached (4096 cells). Erase unused edits first.");
        recipe.Version = 2;
        recipe.Settings = null; // Additions always inherit the map's infantry profile.
        if (existing != null)
            recipe.Cells.Remove(existing);
        if (mode != "Erase")
            recipe.Cells.Add(
                new()
                {
                    X = x,
                    Z = z,
                    Y = y,
                    Mode = mode,
                }
            );
    }

    public static bool Contains(MapNavigationCell cell, float x, float y, float z, float tolerance = .001f) =>
        x >= cell.X - tolerance
        && x <= cell.X + 1 + tolerance
        && z >= cell.Z - tolerance
        && z <= cell.Z + 1 + tolerance
        && Math.Abs(y - cell.Y) <= FloorTolerance;

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 100000;

    private static bool Point(SpatialVector? p) => p != null && Finite(p.X) && Finite(p.Y) && Finite(p.Z);

    private static float DistanceSquared(SpatialVector a, SpatialVector b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);
}
