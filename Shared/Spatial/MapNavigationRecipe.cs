namespace WTT.Campaigns.Shared.Spatial;

// Saved inputs only. Native baked data and registrations never belong to a document.
// This first recipe version is used by the dedicated editor, not automatic raid loading.
public sealed class MapNavigationRecipe
{
    public int Version { get; set; } = 1;
    public SpatialVector? TestLocation { get; set; }
    public MapNavigationSettings? Settings { get; set; }
    public List<MapNavigationCell> Cells { get; set; } = new();
    public List<MapNavigationConnection> Connections { get; set; } = new();

    public bool ShouldSerializeCells() => Cells.Count > 0;

    public bool ShouldSerializeConnections() => Connections.Count > 0;

    public bool ShouldSerializeTestLocation() => TestLocation != null;

    public bool ShouldSerializeSettings() => Settings != null;
}

public sealed class MapNavigationSettings
{
    public float Radius { get; set; } = .5f;
    public float Height { get; set; } = 2;
    public float Slope { get; set; } = 45;
    public float Step { get; set; } = .4f;
    public float VoxelSize { get; set; } = .16666667f;
    public int TileSize { get; set; } = 256;
}

public static class MapNavigationRules
{
    public const int Format = 13;

    public static List<string> Errors(MapNavigationRecipe? recipe)
    {
        var errors = new List<string>();
        if (recipe == null)
            return errors;
        if (recipe.Version is not (1 or 2))
            errors.Add("Unsupported navigation recipe version.");
        errors.AddRange(MapNavigationPainting.Errors(recipe));
        if (
            recipe.TestLocation is { } point
            && (!point.Finite || Math.Abs(point.X) > 100000 || Math.Abs(point.Y) > 100000 || Math.Abs(point.Z) > 100000)
        )
            errors.Add("Navigation test location must be finite and within map coordinate limits.");
        if (recipe.Settings is not { } settings)
            return errors;
        void Range(float value, float low, float high, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < low || value > high)
                errors.Add($"Navigation {name} must be between {low} and {high}.");
        }
        Range(settings.Radius, .1f, 2, "radius");
        Range(settings.Height, .5f, 4, "height");
        Range(settings.Slope, 0, 60, "slope");
        Range(settings.Step, 0, 1.5f, "step height");
        Range(settings.VoxelSize, .025f, .5f, "voxel size");
        if (settings.Step >= settings.Height)
            errors.Add("Navigation step height must be less than standing height.");
        if (settings.TileSize < 16 || settings.TileSize > 1024 || (settings.TileSize & (settings.TileSize - 1)) != 0)
            errors.Add("Navigation tile size must be a power of two from 16 to 1024.");
        return errors;
    }
}
