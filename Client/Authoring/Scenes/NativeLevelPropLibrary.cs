using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Locally generated prefabs from serialized level files. No unloaded scene is activated.
internal static class NativeLevelPropLibrary
{
    internal const string Prefix = "campaigns/level-props/";

    private sealed class Entry
    {
        public string Id = "",
            Name = "",
            Source = "",
            Scene = "",
            Error = "",
            Bundle = "",
            Asset = "";
        public List<string> Sources = new(),
            Aliases = new();
    }

    private sealed class BundleInfo
    {
        public string File = "",
            Sha256 = "";
        public List<string> Dependencies = new();
        public Dictionary<string, List<string>> Assets = new();
    }

    private sealed class Manifest
    {
        public int Schema;
        public List<object> Levels = new();
        public Dictionary<string, BundleInfo> Bundles = new();
        public List<Entry> Entries = new();

        [JsonIgnore]
        public SceneCatalogEntry[] Catalog = Array.Empty<SceneCatalogEntry>();

        [JsonIgnore]
        public Dictionary<string, Entry> ByAsset = new(StringComparer.Ordinal);
    }

    private sealed class Loaded
    {
        internal int References;
        internal AssetBundle? Bundle;
        internal Task? Loading;
    }

    private static readonly string DirectoryPath = Path.Combine(
        Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!,
        "LevelPropLibrary"
    );
    private static Task<Manifest>? _reading;
    private static Manifest? _manifest;
    private static SceneCatalogEntry[] _entries = Array.Empty<SceneCatalogEntry>();
    private static readonly Dictionary<string, Loaded> Bundles = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (long Length, long Modified, string Hash)> Verified = new(StringComparer.Ordinal);
    internal static int LevelCount => _manifest?.Levels.Count ?? 0;

    internal static string SourceDescription(MapTarget target) =>
        _manifest != null && _manifest.ByAsset.TryGetValue(target.Asset, out var entry)
            ? Path.GetFileNameWithoutExtension(entry.Scene).Replace('_', ' ') + " · " + entry.Source
            : target.Path;

    internal static async Task<SceneCatalogEntry[]> Entries()
    {
        if (_manifest != null)
            return _entries;
        _reading ??= Task.Run(() =>
        {
            var value = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Path.Combine(DirectoryPath, "catalog.json")));
            if (value?.Schema != 1)
                throw new InvalidOperationException("The level prop library needs rebuilding.");
            foreach (var pair in value.Bundles)
                if (!SafeName(pair.Key) || pair.Value.File != pair.Key + ".bundle" || pair.Value.Sha256.Length != 64)
                    throw new InvalidOperationException("Invalid level prop bundle metadata.");
            foreach (var entry in value.Entries)
                if (
                    !SafeName(entry.Bundle)
                    || !SceneAssetRules.SafePath(entry.Asset)
                    || (entry.Error.Length == 0 && !value.Bundles.ContainsKey(entry.Bundle))
                )
                    throw new InvalidOperationException("Invalid level prop entry metadata.");
            var entries = new List<SceneCatalogEntry>();
            foreach (var item in value.Entries)
            {
                var bundle = Prefix + "prop-" + item.Id + ".bundle";
                var id = SceneAssetRules.Identity(bundle, item.Asset);
                value.ByAsset.Add(item.Asset, item);
                entries.Add(
                    new SceneCatalogEntry
                    {
                        Id = id,
                        Name = item.Name,
                        Error = item.Error,
                        AssetTarget = new MapTarget
                        {
                            Kind = "AssetProp",
                            Bundle = bundle,
                            Asset = item.Asset,
                            Fingerprint = id,
                            Path = item.Scene + " · " + item.Source,
                        },
                    }
                );
            }
            value.Catalog = entries.ToArray();
            return value;
        });
        Manifest manifest;
        try
        {
            manifest = await _reading;
        }
        catch
        {
            _reading = null;
            throw;
        }
        // Multiple callers may await the same read. Publish the complete snapshot only once.
        if (_manifest != null)
            return _entries;
        _entries = manifest.Catalog;
        _manifest = manifest;
        return _entries;
    }

    private static bool SafeName(string value) =>
        value.Length > 0 && value.Length <= 100 && value.AsValueEnumerable().All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    internal static int Compare(SceneCatalogEntry a, SceneCatalogEntry b)
    {
        var order = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
    }

    internal static async Task<SceneCatalogEntry[]> Search(string search, bool hideUnavailable, CancellationToken token)
    {
        var entries = await Entries();
        var manifest = _manifest!;
        return await Task.Run(() =>
        {
            var found = new List<SceneCatalogEntry>();
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                if (hideUnavailable && entry.Error.Length > 0)
                    continue;
                var source = manifest.ByAsset[entry.AssetTarget!.Asset];
                if (
                    search.Length == 0
                    || entry.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || source.Source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                    || source.Sources.Exists(s => s.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    || source.Aliases.Exists(s => s.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                )
                    found.Add(entry);
            }
            found.Sort(Compare);
            token.ThrowIfCancellationRequested();
            return found.ToArray();
        });
    }

    internal static async Task<SceneAssetCatalog.Model> Load(MapTarget target, CancellationToken token, bool previewOnly)
    {
        await Entries();
        token.ThrowIfCancellationRequested();
        if (
            !SceneAssetRules.Valid(target)
            || target.Kind != "AssetProp"
            || !_manifest!.ByAsset.TryGetValue(target.Asset, out var entry)
            || target.Bundle != Prefix + "prop-" + entry.Id + ".bundle"
            || entry.Error.Length > 0
        )
            throw new InvalidOperationException("Unknown or unavailable level prop.");
        var order = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name)
        {
            if (visited.Contains(name))
                return;
            if (!visiting.Add(name) || !_manifest!.Bundles.TryGetValue(name, out var info))
                throw new InvalidOperationException("Invalid level prop dependency graph.");
            var dependencies = info.Dependencies;
            if (name == entry.Bundle && !info.Assets.TryGetValue(entry.Asset, out dependencies))
                throw new InvalidOperationException("Missing level prop resource manifest.");
            foreach (var dependency in dependencies)
                Visit(dependency);
            visiting.Remove(name);
            visited.Add(name);
            order.Add(name);
        }
        Visit(entry.Bundle);
        var retained = new List<(string Name, Loaded State)>();
        var model = new SceneAssetCatalog.Model { ReleaseLibrary = () => Release(retained) };
        try
        {
            foreach (var name in order)
            {
                if (!Bundles.TryGetValue(name, out var state))
                    Bundles[name] = state = new Loaded();
                state.References++;
                retained.Add((name, state));
                await (state.Loading ??= LoadBundle(name, state));
                token.ThrowIfCancellationRequested();
            }
            var request = Bundles[entry.Bundle].Bundle!.LoadAssetAsync<GameObject>(entry.Asset);
            while (!request.isDone)
                await UniTask.NextFrame();
            token.ThrowIfCancellationRequested();
            var prefab = request.asset as GameObject;
            if (!prefab)
                throw new InvalidOperationException("The generated level prop is missing.");
            if (previewOnly)
                model.Object = ScenePreviewModel.Copy(prefab!.transform);
            else
            {
                var error = SceneAssetCatalog.Restriction(prefab!, false);
                if (error.Length > 0)
                    throw new InvalidOperationException(error);
                var wrapper = new GameObject("CampaignEditor level prop");
                wrapper.SetActive(false);
                model.Object = wrapper;
                var clone = UnityEngine.Object.Instantiate(prefab, wrapper.transform, false);
                model.SelectionGeometry = clone!.transform;
                clone.SetActive(true);
            }
            return model;
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    private static async Task LoadBundle(string name, Loaded state)
    {
        var info = _manifest!.Bundles[name];
        var path = Path.Combine(DirectoryPath, info.File);
        await Task.Run(() =>
        {
            var fileInfo = new FileInfo(path);
            var stamp = (fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks, info.Sha256);
            lock (Verified)
                if (Verified.TryGetValue(path, out var prior) && prior == stamp)
                    return;
            using var file = File.OpenRead(path);
            using var sha = SHA256.Create();
            if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != info.Sha256)
                throw new InvalidOperationException("The level prop library does not match its validated catalog: " + name);
            lock (Verified)
                Verified[path] = stamp;
        });
        var request = AssetBundle.LoadFromFileAsync(path);
        while (!request.isDone)
            await UniTask.NextFrame();
        state.Bundle = request.assetBundle
            ? request.assetBundle
            : throw new InvalidOperationException("Level prop bundle failed to load: " + name);
    }

    private static void Release(List<(string Name, Loaded State)> retained)
    {
        foreach (var item in retained)
            item.State.References--;
        _ = UnloadAfterDestruction(retained);
    }

    private static async Task UnloadAfterDestruction(List<(string Name, Loaded State)> retained)
    {
        await UniTask.NextFrame();
        for (var i = retained.Count - 1; i >= 0; i--)
        {
            var (name, state) = retained[i];
            if (state.References != 0 || !Bundles.TryGetValue(name, out var current) || current != state)
                continue;
            if (state.Bundle)
                state.Bundle!.Unload(true);
            Bundles.Remove(name);
        }
    }
}
