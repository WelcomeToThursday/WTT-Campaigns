using EFT.Interactive;
using UnityEngine;
using UnityEngine.SceneManagement;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Spatial;

internal sealed class LevelZoneRuntime : IDisposable
{
    private readonly List<GameObject> _roots = new();
    private static readonly Dictionary<string, GameObject> Active = new(StringComparer.Ordinal);

    internal static GameObject? Find(string id) => Active.TryGetValue(id, out var root) && root ? root : null;

    internal void Apply(IEnumerable<MapLayerContent> content)
    {
        var ids = Resources
            .FindObjectsOfTypeAll<TriggerWithId>()
            .AsValueEnumerable()
            .Where(t => t && t.gameObject.scene.IsValid())
            .Select(t => t.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var layer in content)
        foreach (var zone in layer.Zones)
        {
            if (zone.Location != ZoneRuntime.Location || !ids.Add(zone.Id))
                throw new InvalidOperationException("Level zone has a conflicting identity or map: " + layer.Source + "/" + zone.Id);
            var scene = SceneManager.GetSceneByName(zone.Scene);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Level zone scene is unavailable: " + zone.Scene);
            var root = ZoneRuntime.Volume(zone);
            _roots.Add(root);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<NativeZoneBridge>().Initialize(zone);
            HazardRuntime.Attach(root, zone);
            Active.Add(zone.Id, root);
        }
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            if (!root)
                continue;
            root.GetComponent<HazardRuntime>()?.Clear();
            root.GetComponent<NativeZoneBridge>()?.Clear();
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
        foreach (var key in Active.AsValueEnumerable().Where(p => _roots.Contains(p.Value)).Select(p => p.Key).ToArray())
            Active.Remove(key);
        _roots.Clear();
    }
}
