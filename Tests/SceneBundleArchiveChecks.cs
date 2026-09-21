using System.Text;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Tests;

internal static class SceneBundleArchiveChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var compression in new[] { 0, 2, 3 })
        foreach (var atEnd in new[] { false, true })
        {
            using var ordinary = Fixture(new[] { "CAB-assets", "CAB-assets.resS" }, compression, atEnd);
            check(SceneBundleArchive.Read(ordinary) == SceneBundleArchive.Contents.Assets, "Ordinary archive reads compression/end flags");
            using var scene = Fixture(new[] { "CAB-scene.sharedAssets", "CAB-scene" }, compression, atEnd);
            check(SceneBundleArchive.Read(scene) == SceneBundleArchive.Contents.Scene, "Scene archive is rejected before native loading");
        }
        using var overlap = Fixture(new[] { "CAB-assets" }, 3, false, overlap: true);
        check(SceneBundleArchive.Read(overlap) == SceneBundleArchive.Contents.Assets, "Overlapping LZ4 matches decode directory bytes");
        foreach (var names in new[] { Array.Empty<string>(), new[] { "CAB-a", "CAB-b" }, new[] { "CAB-a.resS" } })
        {
            using var unknown = Fixture(names, 0, false);
            check(Rejected(unknown), "Unknown archive layouts fail closed");
        }
        using var truncated = Fixture(new[] { "CAB-a" }, 0, false);
        truncated.SetLength(truncated.Length - 1);
        check(Rejected(truncated), "Truncated archives cannot reach the native loader");
        using var malformed = Fixture(new[] { "CAB-a" }, 3, false, payload: new byte[] { 0xf0 });
        check(Rejected(malformed), "Truncated LZ4 extended lengths fail closed");
        using var zeroOffset = Fixture(new[] { "CAB-a" }, 3, false, payload: new byte[] { 0x10, 0, 0, 0 });
        check(Rejected(zeroOffset), "LZ4 cannot copy from zero offset");
        using var beforeStart = Fixture(new[] { "CAB-a" }, 3, false, payload: new byte[] { 0x10, 0, 2, 0 });
        check(Rejected(beforeStart), "LZ4 cannot read before the output buffer");
        using var unsupported = Fixture(new[] { "CAB-a" }, 1, false);
        check(Rejected(unsupported), "Unknown compression is unavailable rather than guessed safe");
    }

    internal static void Installed(string game)
    {
        var root = Path.Combine(game, "EscapeFromTarkov_Data", "StreamingAssets", "Windows");
        var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, "Windows.json")));
        var paths = manifest.Properties().ToDictionary(p => p.Name, p => Path.Combine(root, p.Name), StringComparer.Ordinal);
        var mods = Path.Combine(game, "SPT_Runtime", "user", "mods");
        if (Directory.Exists(mods))
            foreach (var mod in Directory.EnumerateDirectories(mods).Order(StringComparer.Ordinal))
            {
                var file = Path.Combine(mod, "bundles.json");
                if (!File.Exists(file))
                    continue;
                var entries = JObject.Parse(File.ReadAllText(file))["manifest"] as JArray;
                foreach (var entry in entries ?? new JArray())
                {
                    var key = (string?)entry["key"];
                    if (!string.IsNullOrEmpty(key))
                        paths[key] = Path.Combine(mod, "bundles", key);
                }
            }
        var scenes = new HashSet<string>(StringComparer.Ordinal);
        var ordinaryMaps = 0;
        foreach (var (key, path) in paths)
        {
            var contents = SceneBundleArchive.Read(path);
            if (contents == SceneBundleArchive.Contents.Scene)
                scenes.Add(key);
            else if (key.StartsWith("maps/", StringComparison.Ordinal))
                ordinaryMaps++;
        }
        if (!scenes.Contains("assets/commonassets/scenes/emptyscene.bundle") || !scenes.Contains("dissonancesetup") || ordinaryMaps == 0)
            throw new InvalidOperationException("Installed bundle classification missed scene or ordinary map archives.");
        Console.WriteLine(
            $"Scene archive preflight: {paths.Count} registered bundles read; {scenes.Count} scene archives excluded; {ordinaryMaps} ordinary maps/ bundles eligible."
        );
    }

    private static bool Rejected(Stream stream)
    {
        try
        {
            SceneBundleArchive.Read(stream);
            return false;
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            return true;
        }
    }

    private static MemoryStream Fixture(string[] names, int compression, bool atEnd, bool overlap = false, byte[]? payload = null)
    {
        static void UInt(BinaryWriter writer, uint value)
        {
            writer.Write((byte)(value >> 24));
            writer.Write((byte)(value >> 16));
            writer.Write((byte)(value >> 8));
            writer.Write((byte)value);
        }
        static void Long(BinaryWriter writer, long value)
        {
            UInt(writer, (uint)(value >> 32));
            UInt(writer, (uint)value);
        }
        static void Text(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.UTF8.GetBytes(value));
            writer.Write((byte)0);
        }
        using var metadata = new MemoryStream();
        using (var writer = new BinaryWriter(metadata, Encoding.UTF8, true))
        {
            writer.Write(new byte[16]);
            UInt(writer, 0); // No data blocks needed for a directory-only fixture.
            UInt(writer, (uint)names.Length);
            foreach (var name in names)
            {
                Long(writer, 0);
                Long(writer, 0);
                UInt(writer, name.EndsWith(".resS", StringComparison.Ordinal) ? 0u : 4u);
                Text(writer, name);
            }
        }
        var bytes = metadata.ToArray();
        var packed = bytes;
        if (compression is 2 or 3)
        {
            using var encoded = new MemoryStream();
            var start = 0;
            if (overlap)
            {
                encoded.Write(new byte[] { 0x1b, 0, 1, 0 }); // One zero literal, then fifteen overlapping zeros.
                start = 16;
            }
            encoded.WriteByte(0xf0);
            var remaining = bytes.Length - start - 15;
            while (remaining >= 255)
            {
                encoded.WriteByte(255);
                remaining -= 255;
            }
            encoded.WriteByte((byte)remaining);
            encoded.Write(bytes, start, bytes.Length - start);
            packed = encoded.ToArray();
        }
        packed = payload ?? packed;
        var file = new MemoryStream();
        using (var writer = new BinaryWriter(file, Encoding.UTF8, true))
        {
            Text(writer, "UnityFS");
            UInt(writer, 7);
            Text(writer, "5.x.x");
            Text(writer, "2022.3.43f1");
            var sizePosition = file.Position;
            Long(writer, 0);
            UInt(writer, (uint)packed.Length);
            UInt(writer, (uint)bytes.Length);
            UInt(writer, (uint)(0x40 | compression | (atEnd ? 0x80 : 0)));
            while (file.Position % 16 != 0)
                writer.Write((byte)0);
            if (atEnd)
                writer.Write(new byte[16]);
            writer.Write(packed);
            var length = file.Length;
            file.Position = sizePosition;
            Long(writer, length);
        }
        file.Position = 0;
        return file;
    }
}
