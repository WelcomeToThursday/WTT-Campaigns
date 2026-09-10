using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Seasons;

namespace WTT.Campaigns.Tests;

internal static class SeasonItemBundleChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "season-item-bundles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var items = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data/season-items.json")));
            var keys = items.Properties().Select(p => (string)p.Value["_props"]!["Prefab"]!["path"]!).Distinct().ToArray();
            var backport = Path.Combine(root, "renamed-backport");
            var seasonal = Path.Combine(root, "seasonal");
            void WriteMod(string directory, IEnumerable<string> bundleKeys)
            {
                Directory.CreateDirectory(directory);
                var manifest = new JArray();
                foreach (var key in bundleKeys)
                {
                    var file = Path.Combine(directory, "bundles", key);
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.WriteAllText(file, "fixture");
                    manifest.Add(new JObject { ["key"] = key, ["dependencyKeys"] = new JArray() });
                }

                File.WriteAllText(Path.Combine(directory, "bundles.json"), new JObject { ["manifest"] = manifest }.ToString());
            }

            WriteMod(backport, keys);
            WriteMod(seasonal, keys.Select(k => "wtt-campaigns/" + k).Append("custom/model.bundle"));
            var bundles = new SeasonItemBundles([seasonal, backport]);
            foreach (var key in keys)
            {
                check(bundles.Resolve(key) == key, "Imported item uses the shared bundle: " + key);
                check(bundles.Resolve("wtt-campaigns/" + key) == key, "Older packs prefer Backport over duplicate assets: " + key);
            }

            check(bundles.Resolve("custom/model.bundle") == "custom/model.bundle", "Authored bundles retain their registered keys");
            File.Delete(Path.Combine(backport, "bundles", keys[0]));
            bundles = new SeasonItemBundles([backport]);
            check(bundles.Resolve(keys[0]) == null, "A manifest entry without its bundle cannot register an item");
            check(bundles.Resolve("unknown.bundle") == null, "Unavailable item models remain unavailable");
            File.Delete(Path.Combine(backport, "bundles.json"));
            check(new SeasonItemBundles([backport]).Resolve(keys[1]) == null, "Unregistered loose bundles cannot register items");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
