using System.Threading;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.DependencyManager;
using Diz.Resources;
using EFT;
using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;
using Object = UnityEngine.Object;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Loads only explicitly requested assets; there is no runtime catalog-wide bundle scan.
internal static class SceneAssetCatalog
{
    private sealed class SceneArchiveException(string message) : InvalidOperationException(message);

    private static async UniTask InspectArchives(
        IEasyAssets assets,
        string key,
        Dictionary<string, SceneBundleArchive.Contents> archives,
        Func<UniTask> budget
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(key);
        while (pending.Count > 0)
        {
            await budget();
            var next = pending.Pop();
            if (!seen.Add(next))
                continue;
            if (!assets.System.Nodes.TryGetValue(next, out var node) || node.Data is not EasyBundle bundle)
                throw new InvalidOperationException("A required asset bundle is missing: " + next);
            if (!archives.TryGetValue(next, out var contents))
            {
                contents = bundle._bundle
                    ? bundle._bundle.isStreamedSceneAssetBundle
                        ? SceneBundleArchive.Contents.Scene
                        : SceneBundleArchive.Contents.Assets
                    : SceneBundleArchive.Read(bundle._path);
                archives[next] = contents;
            }
            if (contents == SceneBundleArchive.Contents.Scene)
                throw new SceneArchiveException("This asset requires a streamed map scene: " + next);
            foreach (var dependency in bundle.DependencyKeys)
                pending.Push(dependency);
        }
    }

    private static async UniTask WaitForBundle(DependencyGraph<IEasyBundle>.Token lease, Action<int>? progress = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Exception? failure = null;
        while (!lease.LoadingJob.IsCompleted)
        {
            // A native loader fault does not always complete the dependency graph's token.
            // Observe that fault, but let every already-started native request finish before release.
            var loading = false;
            foreach (var node in lease.Nodes)
            {
                if (node.Data is not EasyBundle bundle || bundle._loadingJob == null)
                    continue;
                loading |= !bundle._loadingJob.IsCompleted;
                if (bundle._loadingJob.IsFaulted)
                    failure ??= bundle._loadingJob.Exception;
            }
            if (failure != null && !loading)
                throw new InvalidOperationException("Native asset loading failed.", failure);
            progress?.Invoke((int)watch.Elapsed.TotalSeconds);
            await UniTask.NextFrame();
        }
        await lease.LoadingJob;
    }

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

    internal sealed class Model : IDisposable
    {
        internal GameObject Object = null!;
        internal Transform? SelectionGeometry;
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

    internal static async Task<Model> Load(MapTarget target, CancellationToken token, bool previewOnly = false)
    {
        if (target.Bundle.StartsWith(NativeLevelPropLibrary.Prefix, StringComparison.Ordinal))
            return await NativeLevelPropLibrary.Load(target, token, previewOnly);
        if (target.Bundle == NativeContainerLibrary.BundleKey)
            return await NativeContainerLibrary.Load(target, token, previewOnly);
        if (!target.IsAsset)
            return previewOnly ? MapSceneAdapter.LoadScenePreview(target) : MapSceneAdapter.LoadSceneCopy(target);
        if (!SceneAssetRules.Valid(target))
            throw new InvalidOperationException("Invalid saved asset reference.");
        var assets = Singleton<ObjectsFactory>.Instance.EasyAssets;
        if (!assets.System.Nodes.TryGetValue(target.Bundle, out var node) || node.Data is not EasyBundle bundle)
            throw new InvalidOperationException("The installed asset bundle is missing: " + target.Bundle);
        var result = new Model();
        try
        {
            var slice = System.Diagnostics.Stopwatch.StartNew();
            await InspectArchives(
                assets,
                target.Bundle,
                new Dictionary<string, SceneBundleArchive.Contents>(StringComparer.Ordinal),
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    if (slice.Elapsed.TotalMilliseconds < 2)
                        return;
                    await UniTask.NextFrame(cancellationToken: token);
                    slice.Restart();
                }
            );
            token.ThrowIfCancellationRequested();
            result.Lease = assets.Retain(new[] { target.Bundle }, ct: token);
            await WaitForBundle(result.Lease);
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
            if (previewOnly)
            {
                result.Object = ScenePreviewModel.Copy(prefab.transform);
                return result;
            }
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
