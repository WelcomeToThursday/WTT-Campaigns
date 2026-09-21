namespace WTT.Campaigns.Shared.Spatial;

public sealed class MapTerrainRecipe
{
    public MapTerrainTarget Target { get; set; } = new();
    public List<MapTerrainStroke> Strokes { get; set; } = new();
}

public sealed class MapTerrainTarget
{
    public string Scene { get; set; } = "";
    public string Path { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public float Width { get; set; }
    public float Depth { get; set; }
    public int Textures { get; set; }
    public int Grass { get; set; }
}

public sealed class MapTerrainStroke
{
    public string Mode { get; set; } = "Texture";
    public int Layer { get; set; }
    public float Radius { get; set; } = 2;
    public float Strength { get; set; } = .5f;
    public float Falloff { get; set; } = .5f;
    public int Density { get; set; } = 8;

    // Explicit, equally spaced stamps in terrain-local metres. No frame-time dependence on replay.
    public List<SpatialVector> Points { get; set; } = new();
}

public static class MapTerrainPainting
{
    public const int Format = 14;
    public const int MaxRecipes = 128;
    public const int MaxStrokes = 2048;
    public const int MaxPoints = 32768;
    public const int MaxDensity = 16;

    public static bool IsTexture(string mode) => mode is "Texture" or "RestoreTexture";

    public static List<string> Errors(List<MapTerrainRecipe>? recipes)
    {
        var errors = new List<string>();
        if (recipes == null)
            return errors;
        if (recipes.Count > MaxRecipes)
            errors.Add("Terrain paint exceeds 128 recipes.");
        var strokes = 0;
        var points = 0;
        var targets = new Dictionary<string, string>();
        foreach (var recipe in recipes)
        {
            var target = recipe?.Target;
            if (
                target == null
                || string.IsNullOrWhiteSpace(target.Scene)
                || target.Scene.Length > 256
                || string.IsNullOrWhiteSpace(target.Path)
                || target.Path.Length > 4096
                || target.Fingerprint == null
                || target.Fingerprint.Length != 64
                || !Hex(target.Fingerprint)
                || !Unit(target.Width, .01f, 100000)
                || !Unit(target.Depth, .01f, 100000)
                || target.Textures is < 0 or > 32
                || target.Grass is < 0 or > 128
                || recipe!.Strokes == null
            )
            {
                errors.Add("Terrain paint has an invalid terrain binding.");
                continue;
            }
            var key = target.Scene + "|" + target.Path;
            if (targets.TryGetValue(key, out var fingerprint) && fingerprint != target.Fingerprint)
                errors.Add("Terrain recipes disagree about the same tile's assets.");
            targets[key] = target.Fingerprint;
            strokes += recipe.Strokes.Count;
            foreach (var stroke in recipe.Strokes)
            {
                if (
                    stroke == null
                    || stroke.Mode is not ("Texture" or "RestoreTexture" or "AddGrass" or "RemoveGrass" or "ClearGrass" or "RestoreGrass")
                    || !Unit(stroke.Radius, .5f, 20)
                    || !Unit(stroke.Strength, .01f, 1)
                    || !Unit(stroke.Falloff, 0, 1)
                    || stroke.Density is < 0 or > MaxDensity
                    || stroke.Points == null
                    || stroke.Layer < 0
                    || (IsTexture(stroke.Mode) ? stroke.Layer >= target.Textures : stroke.Layer >= target.Grass)
                )
                {
                    errors.Add("Terrain paint has an invalid brush or palette selection.");
                    continue;
                }
                points += stroke.Points.Count;
                if (
                    stroke.Points.Count == 0
                    || stroke.Points.Exists(p => p == null || !Unit(p.X, 0, target.Width) || !Unit(p.Z, 0, target.Depth) || p.Y != 0)
                )
                    errors.Add("Terrain stamps must lie inside their bound tile.");
            }
        }
        if (strokes > MaxStrokes || points > MaxPoints)
            errors.Add("Terrain paint exceeds 2048 strokes or 32768 stamps. Remove unused strokes before painting more.");
        return errors;
    }

    private static bool Unit(float value, float min, float max) => !float.IsNaN(value) && value >= min && value <= max;

    private static bool Hex(string value)
    {
        foreach (var c in value)
            if (!(c >= '0' && c <= '9' || c >= 'A' && c <= 'F'))
                return false;
        return true;
    }

    public static (int X, int Y, int Width, int Height) Region(
        SpatialVector p,
        float radius,
        float tileWidth,
        float tileDepth,
        int width,
        int height
    )
    {
        var x = Math.Clamp((int)Math.Floor((p.X - radius) * width / tileWidth), 0, width);
        var y = Math.Clamp((int)Math.Floor((p.Z - radius) * height / tileDepth), 0, height);
        var right = Math.Clamp((int)Math.Ceiling((p.X + radius) * width / tileWidth), 0, width);
        var top = Math.Clamp((int)Math.Ceiling((p.Z + radius) * height / tileDepth), 0, height);
        return (x, y, right - x, top - y);
    }

    public static float Weight(float x, float z, SpatialVector point, MapTerrainStroke stroke)
    {
        var dx = x - point.X;
        var dz = z - point.Z;
        var distance = (float)Math.Sqrt(dx * dx + dz * dz) / stroke.Radius;
        if (distance >= 1)
            return 0;
        var edge = stroke.Falloff <= 0 ? 1 : Math.Clamp((1 - distance) / stroke.Falloff, 0, 1);
        return stroke.Strength * edge * edge * (3 - 2 * edge);
    }

    public static void Blend(float[] current, float[] original, int layer, float weight, bool restore)
    {
        var total = 0f;
        for (var i = 0; i < current.Length; i++)
        {
            var target =
                restore ? original[i]
                : i == layer ? 1
                : 0;
            current[i] = Math.Max(0, weight >= 1 ? target : current[i] + (target - current[i]) * weight);
            total += current[i];
        }
        for (var i = 0; i < current.Length; i++)
            current[i] =
                total > 0 ? current[i] / total
                : i == layer ? 1
                : 0;
    }

    public static float GrassDensity(float current, int original, float weight, MapTerrainStroke stroke)
    {
        var target =
            stroke.Mode == "RestoreGrass" ? original
            : stroke.Mode == "AddGrass" ? Math.Max(current, stroke.Density)
            : 0;
        // Preserve native densities above the authoring cap when restoring/removing existing grass.
        return Math.Max(0, current + (target - current) * weight);
    }

    public static int Dominant(float[] weights)
    {
        var best = 0;
        for (var i = 1; i < weights.Length; i++)
            if (weights[i] > weights[best])
                best = i;
        return best;
    }

    public static void CopyDensityCell(int[] cell, int side, int cellX, int cellZ, int[,] target)
    {
        if (
            cell == null
            || side <= 0
            || (long)side * side != cell.Length
            || cellX < 0
            || cellZ < 0
            || (long)(cellX + 1) * side > target.GetLength(1)
            || (long)(cellZ + 1) * side > target.GetLength(0)
        )
            throw new InvalidOperationException("Native grass density cell dimensions changed.");
        for (var z = 0; z < side; z++)
        for (var x = 0; x < side; x++)
            target[cellZ * side + z, cellX * side + x] = cell[z * side + x];
    }
}
