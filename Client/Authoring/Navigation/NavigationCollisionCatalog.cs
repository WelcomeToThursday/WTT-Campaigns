using System.Security.Cryptography;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Serialized evidence, not a map-name exemption. Unknown or mismatched records never
// imply that tree collision is disabled. Kept independent of Unity for offline checks.
internal sealed class NavigationCollisionCatalog
{
    public int Schema;
    public SourceFile[] Files = Array.Empty<SourceFile>();
    public TerrainEntry[] Terrains = Array.Empty<TerrainEntry>();

    internal sealed class SourceFile
    {
        public string Path = "",
            Sha256 = "";
    }

    internal sealed class Node
    {
        public string Name = "";
        public int Sibling;
        public float[] Position = Array.Empty<float>(),
            Rotation = Array.Empty<float>(),
            Scale = Array.Empty<float>();
    }

    internal sealed class TerrainEntry
    {
        public string ScenePath = "",
            DataName = "",
            DataFile = "";
        public int BuildIndex,
            Resolution,
            TreeCount;
        public bool? EnableTreeColliders;
        public Node[] Hierarchy = Array.Empty<Node>();
        public float[] Size = Array.Empty<float>();
    }

    internal string Validate()
    {
        if (Schema != 1 || Files == null || Files.Length == 0 || Terrains == null || Terrains.Length == 0)
            return "Missing or unsupported native collision catalogue";
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
            if (
                file == null
                || string.IsNullOrWhiteSpace(file.Path)
                || System.IO.Path.IsPathRooted(file.Path)
                || file.Path.Contains(":")
                || file.Path.Contains("\\")
                || !SafePath(file.Path)
                || !paths.Add(file.Path)
                || file.Sha256 == null
                || file.Sha256.Length != 64
                || !Hex(file.Sha256)
            )
                return "Invalid native collision source file";
        var owners = new HashSet<string>(StringComparer.Ordinal);
        if (!paths.Contains("globalgamemanagers") || !paths.Contains("Managed/UnityEngine.TerrainPhysicsModule.dll"))
            return "Missing native scene mapping or collision bindings evidence";
        foreach (var entry in Terrains)
        {
            if (
                entry == null
                || string.IsNullOrWhiteSpace(entry.ScenePath)
                || string.IsNullOrWhiteSpace(entry.DataName)
                || entry.BuildIndex < 0
                || entry.Resolution < 2
                || entry.TreeCount < 0
                || !entry.EnableTreeColliders.HasValue
                || !paths.Contains("level" + entry.BuildIndex)
                || !paths.Contains(entry.DataFile)
                || entry.Hierarchy == null
                || entry.Hierarchy.Length == 0
                || !Vector(entry.Size, 3)
                || entry.Size[0] <= 0
                || entry.Size[1] <= 0
                || entry.Size[2] <= 0
            )
                return "Incomplete native terrain collision evidence";
            var key = entry.ScenePath;
            for (var i = 0; i < entry.Hierarchy.Length; i++)
            {
                var node = entry.Hierarchy[i];
                if (
                    node == null
                    || string.IsNullOrWhiteSpace(node.Name)
                    || node.Name.Contains('/')
                    || (i == 0 ? node.Sibling != -1 : node.Sibling < 0)
                    || !Vector(node.Position, 3)
                    || !Vector(node.Rotation, 4)
                    || !Vector(node.Scale, 3)
                )
                    return "Invalid native terrain hierarchy";
                key += "/" + node.Name + "[" + node.Sibling + "]";
            }
            if (!owners.Add(key))
                return "Ambiguous native terrain collision evidence";
        }
        return "";
    }

    private static bool SafePath(string path)
    {
        foreach (var part in path.Split('/'))
            if (part is "" or "." or "..")
                return false;
        return true;
    }

    private static bool Hex(string value)
    {
        foreach (var c in value)
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
    }

    private static bool Vector(float[] values, int length)
    {
        if (values == null || values.Length != length)
            return false;
        foreach (var v in values)
            if (float.IsNaN(v) || float.IsInfinity(v))
                return false;
        return true;
    }

    // File I/O only: safe to run on a worker. The embedded catalogue is immutable.
    internal string VerifyFiles(string dataDirectory)
    {
        var invalid = Validate();
        if (invalid.Length > 0)
            return invalid;
        foreach (var source in Files)
        {
            using var input = File.OpenRead(System.IO.Path.Combine(dataDirectory, source.Path));
            using var sha = SHA256.Create();
            var actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
            if (!string.Equals(actual, source.Sha256, StringComparison.OrdinalIgnoreCase))
                return "Native collision evidence mismatch: " + source.Path;
        }
        return "";
    }
}
