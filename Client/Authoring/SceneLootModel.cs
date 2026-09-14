using System.Threading;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.InventoryLogic;
using UnityEngine;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal static class SceneLootModel
{
    private sealed class Owned
    {
        internal GameObject Model = null!;
        internal readonly List<(Rigidbody Body, bool Kinematic, bool Gravity)> Bodies = new();
        internal readonly List<(Collider Collider, bool Enabled)> Colliders = new();
    }

    private static readonly Dictionary<GameObject, Owned> Models = new();

    internal static Item Item(List<NativeItem> items) =>
        ItemPreviewClient.Build(new ItemPreviewJob { Items = items }, new List<string>(), out _);

    internal static async Task<GameObject> Create(List<NativeItem> items, CancellationToken token)
    {
        var item = Item(items);
        var factory = Singleton<ObjectsFactory>.Instance;
        var resources = new List<ResourceKey>();
        foreach (var record in items)
        {
            if (!Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(record.Template, out var template))
                throw new InvalidOperationException("The installed item template is unavailable: " + record.Template);
            if (template.Prefab != null)
                resources.Add(template.Prefab);
            if (template.UsePrefab != null)
                resources.Add(template.UsePrefab);
        }
        await factory.LoadBundlesAndCreatePools(
            ObjectsFactory.PoolsCategory.Raid,
            ObjectsFactory.AssemblyType.Local,
            resources.AsValueEnumerable().Distinct().ToArray(),
            JobYieldPriority.Immediate,
            null,
            token
        );
        token.ThrowIfCancellationRequested();
        var model = await factory.CreateItemAsync(item, ECameraType.Default, null, false, JobYieldPriority.Immediate, token);
        if (token.IsCancellationRequested)
        {
            Release(model);
            token.ThrowIfCancellationRequested();
        }
        if (!model)
            throw new InvalidOperationException("The installed item model is unavailable.");
        var root = new GameObject("CampaignEditor loot");
        var owned = new Owned { Model = model };
        Models.Add(root, owned);
        try
        {
            model.transform.SetParent(root.transform, false);
            model.SetActive(true);
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true))
            {
                owned.Bodies.Add((body, body.isKinematic, body.useGravity));
                body.isKinematic = true;
                body.useGravity = false;
            }
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            {
                owned.Colliders.Add((collider, collider.enabled));
                collider.enabled = false;
            }
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                throw new InvalidOperationException("Item model has no visible renderer.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;
            return root;
        }
        catch
        {
            Release(root);
            throw;
        }
    }

    internal static void Release(GameObject? model)
    {
        if (ReferenceEquals(model, null))
            return;
        if (model)
            model.SetActive(false);
        if (!Models.TryGetValue(model, out var owned))
        {
            if (model)
                AssetPoolObject.ReturnToPool(model, true);
            return;
        }
        Models.Remove(model);
        if (owned.Model)
        {
            foreach (var body in owned.Bodies)
                if (body.Body)
                {
                    body.Body.isKinematic = body.Kinematic;
                    body.Body.useGravity = body.Gravity;
                }
            foreach (var collider in owned.Colliders)
                if (collider.Collider)
                    collider.Collider.enabled = collider.Enabled;
            owned.Model.transform.SetParent(null, false);
            AssetPoolObject.ReturnToPool(owned.Model, true);
        }
        UnityEngine.Object.Destroy(model);
    }
}
