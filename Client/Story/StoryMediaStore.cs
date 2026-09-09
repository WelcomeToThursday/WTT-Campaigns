using System.Security.Cryptography;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Story;
using UnityEngine;

namespace SeasonalPerks.Client.Story;

internal static class StoryMediaStore
{
    private static readonly Dictionary<string, (AssetBundle Bundle, int Users, string Hash)> Bundles = new(
        StringComparer.OrdinalIgnoreCase
    );

    internal static AssetBundle Open(string relative, string expectedHash)
    {
        var root = Path.GetFullPath(Path.Combine(Plugin.Folder, "StoryMedia")) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (
            !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(path), ".bundle", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException("Story media must be a bundle inside StoryMedia.");
        }
        if (Bundles.TryGetValue(path, out var existing))
        {
            if (!string.Equals(existing.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Conflicting story media checksums: " + relative);
            }
            Bundles[path] = (existing.Bundle, existing.Users + 1, existing.Hash);
            return existing.Bundle;
        }
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Story media checksum failed: " + relative);
            }
        }
        var bundle = AssetBundle.LoadFromFile(path) ?? throw new InvalidDataException("Unable to load story media: " + relative);
        Bundles.Add(path, (bundle, 1, expectedHash));
        return bundle;
    }

    internal static AssetBundle OpenTrader(string traderId)
    {
        var custom = Trader(traderId);
        if (custom != null)
        {
            return Open(custom.Bundle, custom.Sha256);
        }

        var manifest =
            JsonConvert.DeserializeObject<TraderMediaManifest>(File.ReadAllText(Path.Combine(Plugin.Folder, "StoryMedia", "traders.json")))
            ?? throw new InvalidDataException("Invalid trader media manifest.");
        var room = manifest.Rooms.Single(r => r.Trader == traderId);
        return Open(room.Bundle, room.Sha256);
    }

    internal static bool HasTrader(string traderId)
    {
        return Trader(traderId) != null
            || traderId
                is "579dc571d53a0658a154fbec"
                    or "5c0647fdd443bc2504c2d371"
                    or "5a7c2eca46aef81a7ca2145d"
                    or "5935c25fb3acc3127c3d8cd9"
                    or "54cb50c76803fa8b248b4571"
                    or "5ac3b934156ae10c4430e83c"
                    or "58330581ace78e27b8b10cee"
                    or "54cb57776803fa99248b456e";
    }

    internal static StoryMedia? Trader(string traderId) =>
        StoryClient.Current?.Definition?.Media.SingleOrDefault(m => m.Kind == "TraderScene" && m.TraderId == traderId);

    internal static void Close(AssetBundle bundle)
    {
        var entry = Bundles.Single(p => p.Value.Bundle == bundle);
        if (entry.Value.Users > 1)
        {
            Bundles[entry.Key] = (bundle, entry.Value.Users - 1, entry.Value.Hash);
            return;
        }
        Bundles.Remove(entry.Key);
        bundle.Unload(true);
    }

    internal static StoryMedia Find(string id)
    {
        return StoryClient.Current?.Definition?.Media.SingleOrDefault(m => m.Id == id)
            ?? throw new InvalidOperationException("The story media is not registered: " + id);
    }
}
