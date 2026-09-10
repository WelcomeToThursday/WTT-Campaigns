using Newtonsoft.Json.Linq;

namespace WTT.Campaigns.Server.Seasons;

public sealed class SeasonItemBundles
{
    private const string LegacyPrefix = "wtt-campaigns/";
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public SeasonItemBundles(IEnumerable<string> modDirectories)
    {
        foreach (var directory in modDirectories)
        {
            var manifest = Path.Combine(directory, "bundles.json");
            if (!File.Exists(manifest))
            {
                continue;
            }

            var root = Path.GetFullPath(Path.Combine(directory, "bundles")) + Path.DirectorySeparatorChar;
            foreach (var entry in JObject.Parse(File.ReadAllText(manifest))["manifest"] ?? new JArray())
            {
                var key = (string?)entry["key"];
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                var path = Path.GetFullPath(Path.Combine(root, key));
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                {
                    _keys.Add(key);
                }
            }
        }
    }

    public string? Resolve(string path)
    {
        // Old season packs may retain our former prefix. Prefer the shared Backport asset.
        var original = path.StartsWith(LegacyPrefix, StringComparison.Ordinal) ? path.Substring(LegacyPrefix.Length) : path;
        if (_keys.Contains(original))
        {
            return original;
        }

        return _keys.Contains(path) ? path : null;
    }
}
