using System.IO;
using System.Threading;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;
using Object = UnityEngine.Object;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Metadata survives catalog browsing; Unity assets are held only by native dependency tokens.
internal sealed class SceneAssetCatalog : IDisposable
{
    private sealed class CachedBundle
    {
        public string Stamp = "";
        public List<SceneCatalogEntry> Entries = new();
    }

    private readonly Dictionary<string, CachedBundle> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedBundle> _stored = new(StringComparer.Ordinal);
    private bool _rescan;
    private Func<bool>? _active;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _path = Path.Combine(BepInEx.Paths.CachePath, "wtt-scene-assets-v1.json");
    private bool _running;
    private int _scanned;
    internal int Revision { get; private set; }
    internal string Status { get; private set; } = "";
    private SceneCatalogEntry[] _entries = Array.Empty<SceneCatalogEntry>();
    private int _entriesRevision = -1;
    internal IEnumerable<SceneCatalogEntry> Entries
    {
        get
        {
            if (_entriesRevision != Revision)
            {
                _entries = _cache.Values.AsValueEnumerable().SelectMany(v => v.Entries).ToArray();
                _entriesRevision = Revision;
            }
            return _entries;
        }
    }

    internal SceneAssetCatalog()
    {
        try
        {
            if (File.Exists(_path))
                foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, CachedBundle>>(File.ReadAllText(_path)) ?? new())
                    if (
                        pair.Value?.Entries != null
                        && pair.Value.Entries.AsValueEnumerable()
                            .All(e =>
                                e != null
                                && e.AssetTarget != null
                                && SceneAssetRules.SafePath(e.AssetTarget.Bundle)
                                && SceneAssetRules.SafePath(e.AssetTarget.Asset)
                            )
                    )
                        _stored[pair.Key] = pair.Value;
        }
        catch (Exception e)
        {
            Plugin.LogInfo("Scene asset cache: " + e.Message);
        }
    }

    internal void Start(Action changed, Func<bool>? active = null)
    {
        _active = active ?? _active;
        if (!_running && !_lifetime.IsCancellationRequested)
            _ = Scan(changed);
    }

    internal void Retry(string id, Action changed)
    {
        foreach (
            var key in _cache
                .AsValueEnumerable()
                .Where(p => p.Value.Entries.AsValueEnumerable().Any(e => e.Id == id))
                .Select(p => p.Key)
                .ToArray()
        )
        {
            _cache.Remove(key);
            _stored.Remove(key);
        }
        Revision++;
        _rescan = true;
        Start(changed);
    }

    private async Task Scan(Action changed)
    {
        _running = true;
        _rescan = false;
        var token = _lifetime.Token;
        try
        {
            var assets = Singleton<ObjectsFactory>.Instance.EasyAssets;
            var nodes = assets.System.Nodes;
            var keys = nodes
                .Keys.AsValueEnumerable()
                .OrderBy(k => k.IndexOf("location_objects", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(k => k, StringComparer.Ordinal)
                .ToArray();
            // Inventory resources have their own native placement path, including attachments and presets.
            var inventory = new HashSet<string>(StringComparer.Ordinal);
            foreach (var template in Singleton<ItemFactory>.Instance.ItemTemplates.Values)
            {
                if (template.Prefab != null)
                    inventory.Add(template.Prefab.path);
                if (template.UsePrefab != null)
                    inventory.Add(template.UsePrefab.path);
            }
            foreach (var obsolete in _cache.Keys.AsValueEnumerable().Where(k => !nodes.ContainsKey(k) || inventory.Contains(k)).ToArray())
                _cache.Remove(obsolete);
            _scanned = 0;
            foreach (var key in keys)
            {
                token.ThrowIfCancellationRequested();
                while (_active?.Invoke() == false)
                    await UniTask.NextFrame(cancellationToken: token);
                _scanned++;
                if (inventory.Contains(key) || !SceneAssetRules.SafePath(key))
                    continue;
                var bundle = nodes[key].Data as EasyBundle;
                if (bundle == null)
                    continue;
                var stamp = Stamp(assets, key);
                if (_cache.TryGetValue(key, out var cached) && cached.Stamp == stamp)
                    continue;
                if (_stored.TryGetValue(key, out cached) && cached.Stamp == stamp)
                {
                    _cache[key] = cached;
                    _stored.Remove(key);
                    Revision++;
                    await UniTask.NextFrame(cancellationToken: token);
                    continue;
                }
                Status = "Inspecting game assets " + _scanned + " / " + keys.Length;
                await UniTask.NextFrame(cancellationToken: token);
                var found = new CachedBundle { Stamp = stamp };
                if (key.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
                    found.Entries.Add(Unavailable(key, "Map configuration and scene resources cannot be placed as independent props."));
                else
                {
                    DependencyGraph<IEasyBundle>.Token? lease = null;
                    try
                    {
                        lease = assets.Retain(new[] { key }, ct: token);
                        await lease.LoadingJob;
                        token.ThrowIfCancellationRequested();
                        if (!bundle._bundle)
                            throw new InvalidOperationException("Bundle is missing or could not load.");
                        if (bundle._bundle.isStreamedSceneAssetBundle)
                            found.Entries.Add(Unavailable(key, "This asset is embedded in a map scene."));
                        else
                            foreach (var path in bundle._bundle.GetAllAssetNames())
                            {
                                token.ThrowIfCancellationRequested();
                                // The native bundle already holds these objects. Classify the actual resource, including imported model assets.
                                var model = bundle._bundle.LoadAsset<Object>(path) as GameObject;
                                if (!model)
                                    continue;
                                var container = model.GetComponentInChildren<LootableContainer>(true);
                                var target = new MapTarget
                                {
                                    Kind = container ? "AssetContainer" : "AssetProp",
                                    Bundle = key,
                                    Asset = path,
                                    Fingerprint = SceneAssetRules.Identity(key, path),
                                    Template = container ? container.Template ?? "" : "",
                                };
                                found.Entries.Add(
                                    new SceneCatalogEntry
                                    {
                                        Id = target.Fingerprint,
                                        Name = model.name,
                                        AssetTarget = target,
                                        Error = Restriction(model, target.Kind == "AssetContainer"),
                                    }
                                );
                                await UniTask.NextFrame(cancellationToken: token);
                            }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        found.Entries.Add(Unavailable(key, e.Message));
                    }
                    finally
                    {
                        lease?.Release();
                    }
                }
                _cache[key] = found;
                Revision++;
                changed();
                if (_scanned % 100 == 0)
                    Save();
            }
            Status = "Game asset index ready";
            Save();
            changed();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Status = "Asset indexing: " + e.Message;
        }
        finally
        {
            _running = false;
            if (_rescan && !token.IsCancellationRequested)
                Start(changed);
        }
    }

    private static string Stamp(IEasyAssets assets, string key)
    {
        var seen = new HashSet<string>();
        var pending = new Stack<string>();
        var evidence = new List<string>();
        pending.Push(key);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (!seen.Add(next))
                continue;
            if (!assets.System.Nodes.TryGetValue(next, out var node) || node.Data is not EasyBundle b)
            {
                evidence.Add(next + ":missing");
                continue;
            }
            var file = new FileInfo(b._path);
            evidence.Add(next + ":" + (file.Exists ? file.Length + ":" + file.LastWriteTimeUtc.Ticks : "missing"));
            foreach (var dependency in b.DependencyKeys)
                pending.Push(dependency);
        }
        return SceneAssetRules.CacheFingerprint(evidence);
    }

    private static SceneCatalogEntry Unavailable(string key, string reason) =>
        new()
        {
            Id = SceneAssetRules.Identity(key, "unavailable"),
            Name = Path.GetFileNameWithoutExtension(key),
            Error = reason,
            AssetTarget = new MapTarget
            {
                Kind = "AssetProp",
                Bundle = key,
                Asset = "unavailable",
                Fingerprint = SceneAssetRules.Identity(key, "unavailable"),
            },
        };

    internal static string Restriction(GameObject model, bool container)
    {
        if (
            container
            && (
                model.GetComponentInChildren<LootableContainer>(true)?.GetType() != typeof(LootableContainer)
                || !WTT.Campaigns.Shared.Seasons.SeasonValidator.IsId(model.GetComponentInChildren<LootableContainer>(true).Template)
            )
        )
            return "This container has no supported native template.";
        if (!model.GetComponentInChildren<MeshRenderer>(true))
            return "No independently placeable mesh.";
        var components = model.GetComponentsInChildren<Component>(true);
        foreach (var component in components)
        {
            if (!component)
                return "The asset has a missing component.";
            if (
                component
                is Transform
                    or MeshFilter
                    or MeshRenderer
                    or LODGroup
                    or BoxCollider
                    or SphereCollider
                    or CapsuleCollider
                    or MeshCollider
            )
                continue;
            var type = component.GetType().FullName ?? "";
            if (type is "EFT.Ballistics.BallisticCollider" or "PreviewPivot")
                continue;
            if (component is Rigidbody or Light or AudioSource)
                continue;
            if (
                container
                && component is LootableContainer interaction
                && model.GetComponentsInChildren<LootableContainer>(true).Length == 1
            )
            {
                if (interaction.TriggersMap is { Length: > 0 } || interaction._mboitRenderers is { Length: > 0 })
                    return "This container depends on map triggers or registered glass geometry.";
                foreach (
                    var part in new Component[] { interaction.LockHandle, interaction._handle, interaction.Obstacle, interaction.Collider }
                )
                    if (part && !part.transform.IsChildOf(model.transform))
                        return "This container has linked parts outside its prefab.";
                continue;
            }
            if (container && type is "DoorHandle" or "GripPose")
                continue;
            return ScenePropSupport.Restriction(type);
        }
        if (
            components
                .AsValueEnumerable()
                .OfType<Renderer>()
                .Any(r =>
                {
                    var materials = r.sharedMaterials;
                    return materials.Length == 0 || materials.AsValueEnumerable().Any(m => !m || !m.shader);
                })
        )
            return "A required material or shader is missing.";
        if (components.AsValueEnumerable().OfType<MeshFilter>().Any(m => !m.sharedMesh))
            return "A required mesh is missing.";
        if (components.AsValueEnumerable().OfType<Renderer>().Any(r => r.isPartOfStaticBatch))
            return "Combined map geometry cannot be placed independently.";
        return "";
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonConvert.SerializeObject(_cache));
            if (File.Exists(_path))
                File.Delete(_path);
            File.Move(_path + ".tmp", _path);
        }
        catch (Exception e)
        {
            Plugin.LogInfo("Scene asset cache: " + e.Message);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        Save();
    }

    internal sealed class Model : IDisposable
    {
        internal GameObject Object = null!;
        internal DependencyGraph<IEasyBundle>.Token? Lease;
        internal Action? ReleaseLibrary;

        public void Dispose()
        {
            if (Object)
            {
                Object.SetActive(false);
                UnityEngine.Object.Destroy(Object);
            }
            Lease?.Release();
            Lease = null;
            ReleaseLibrary?.Invoke();
            ReleaseLibrary = null;
        }
    }

    internal static async Task<Model> Load(MapTarget target, CancellationToken token)
    {
        if (target.Bundle == NativeContainerLibrary.BundleKey)
            return await NativeContainerLibrary.Load(target, token);
        if (!target.IsAsset)
            return MapSceneAdapter.LoadSceneCopy(target);
        if (!SceneAssetRules.Valid(target))
            throw new InvalidOperationException("Invalid saved asset reference.");
        var assets = Singleton<ObjectsFactory>.Instance.EasyAssets;
        if (!assets.System.Nodes.TryGetValue(target.Bundle, out var node) || node.Data is not EasyBundle bundle)
            throw new InvalidOperationException("The installed asset bundle is missing: " + target.Bundle);
        if (target.Bundle.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Scene-only asset cannot be placed independently.");
        var result = new Model();
        try
        {
            result.Lease = assets.Retain(new[] { target.Bundle }, ct: token);
            await result.Lease.LoadingJob;
            token.ThrowIfCancellationRequested();
            if (!bundle._bundle || bundle._bundle.isStreamedSceneAssetBundle)
                throw new InvalidOperationException("Independent asset bundle is unavailable.");
            var request = bundle._bundle.LoadAssetAsync<GameObject>(target.Asset);
            // Keep the dependency lease until Unity finishes its non-cancellable asset request.
            while (!request.isDone)
                await UniTask.NextFrame();
            token.ThrowIfCancellationRequested();
            var prefab = request.asset as GameObject;
            if (!prefab)
                throw new InvalidOperationException("The saved asset is missing: " + target.Asset);
            var error = Restriction(prefab, target.Kind == "AssetContainer");
            if (error.Length > 0)
                throw new InvalidOperationException(error);
            // An inactive parent prevents native OnEnable registration before a container owns its items and identity.
            var staging = new GameObject("CampaignEditor " + prefab.name);
            staging.SetActive(false);
            result.Object = staging;
            var instance = Object.Instantiate(prefab, staging.transform, false);
            if (instance.GetComponentInChildren<LootableContainer>(true) is { } previewContainer)
                previewContainer.Id = "wtt-preview-" + Guid.NewGuid().ToString("N");
            instance.SetActive(true);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }
}
