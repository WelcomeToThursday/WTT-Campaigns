using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class NativeContainerLibrary
{
    internal const string BundleKey = "campaigns/native-containers.bundle";
    private sealed class Entry
    {
        public string Name = "", Template = "", Asset = "";
    }
    private sealed class Manifest
    {
        public int Schema;
        public string Sha256 = "";
        public List<Entry> Entries = new();
    }
    private static readonly string DirectoryPath = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "ContainerLibrary");
    private static Manifest? _manifest;
    private static AssetBundle? _bundle;
    private static Task<AssetBundle>? _loading;
    private static int _references;

    private static Manifest Catalog()
    {
        if (_manifest != null) return _manifest;
        var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Path.Combine(DirectoryPath, "catalog.json")));
        if (manifest?.Schema != 1) throw new InvalidOperationException("The native container library needs rebuilding.");
        using var file = File.OpenRead(Path.Combine(DirectoryPath, "native-containers.bundle"));
        using var sha = System.Security.Cryptography.SHA256.Create();
        if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") != manifest.Sha256)
            throw new InvalidOperationException("The native container library does not match its validated catalog.");
        return _manifest = manifest;
    }

    internal static IEnumerable<SceneCatalogEntry> Entries()
    {
        foreach (var entry in Catalog().Entries)
            yield return new SceneCatalogEntry
            {
                Id = SceneAssetRules.Identity(BundleKey, entry.Asset), Name = entry.Name,
                AssetTarget = new MapTarget { Kind = "AssetContainer", Bundle = BundleKey, Asset = entry.Asset,
                    Template = entry.Template, Fingerprint = SceneAssetRules.Identity(BundleKey, entry.Asset) },
            };
    }

    internal static async Task<SceneAssetCatalog.Model> Load(MapTarget target, CancellationToken token)
    {
        if (!SceneAssetRules.Valid(target) || target.Bundle != BundleKey
            || !Catalog().Entries.Exists(e => e.Asset == target.Asset && e.Template == target.Template))
            throw new InvalidOperationException("Unknown native container model.");
        _references++;
        var model = new SceneAssetCatalog.Model { ReleaseLibrary = Release };
        try
        {
            var bundle = await (_loading ??= LoadBundle());
            token.ThrowIfCancellationRequested();
            var request = bundle.LoadAssetAsync<GameObject>(target.Asset);
            while (!request.isDone) await UniTask.NextFrame();
            token.ThrowIfCancellationRequested();
            var prefab = request.asset as GameObject;
            if (!prefab) throw new InvalidOperationException("Native container model is missing.");
            var wrapper = new GameObject("CampaignEditor native container");
            wrapper.SetActive(false);
            model.Object = wrapper;
            var clone = UnityEngine.Object.Instantiate(prefab, wrapper.transform, false);
            var containers = clone.GetComponentsInChildren<EFT.Interactive.LootableContainer>(true);
            if (containers.Length != 1 || containers[0].Template != target.Template)
                throw new InvalidOperationException("Native container component does not match the catalog.");
            var container = containers[0];
            container.Id = "wtt-preview-" + Guid.NewGuid().ToString("N");
            container.ItemOwner = null;
            container.IsInitialized = false;
            container.DoorState = EFT.Interactive.EDoorState.Shut;
            clone.SetActive(true);
            return model;
        }
        catch { model.Dispose(); throw; }
    }

    private static async Task<AssetBundle> LoadBundle()
    {
        var request = AssetBundle.LoadFromFileAsync(Path.Combine(DirectoryPath, "native-containers.bundle"));
        while (!request.isDone) await UniTask.NextFrame();
        return _bundle = request.assetBundle ? request.assetBundle : throw new InvalidOperationException("Native container library failed to load.");
    }

    private static void Release()
    {
        if (--_references != 0) return;
        // Instances are destroyed at frame end; unloading their shared resources early
        // would invalidate other native cleanup callbacks in that frame.
        _ = UnloadAfterDestruction();
    }

    private static async Task UnloadAfterDestruction()
    {
        await UniTask.NextFrame();
        if (_references != 0) return;
        if (_bundle) _bundle!.Unload(true);
        _bundle = null;
        _loading = null;
    }
}
