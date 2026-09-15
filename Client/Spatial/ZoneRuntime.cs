using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Spatial;

public sealed class ZoneRuntime : MonoBehaviour
{
    internal static ZoneRuntime? Instance;
    private Player? _player;
    private readonly Dictionary<string, GameObject> _zones = new();
    private readonly HashSet<string> _missionZoneIds = new(StringComparer.Ordinal);
    internal static string Location
    {
        get { return Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.LocationId ?? "" : ""; }
    }

    internal static Vector3 Vector(SpatialVector value)
    {
        return new(value.X, value.Y, value.Z);
    }

    internal static SpatialVector Vector(Vector3 value)
    {
        return new()
        {
            X = value.x,
            Y = value.y,
            Z = value.z,
        };
    }

    internal GameObject? Find(string id)
    {
        return _zones.TryGetValue(id, out var zone) ? zone : null;
    }

    /// <summary>
    /// Installs the server-supplied zones for one active mission.  Layout-owned
    /// zones are kept separate from shared campaign zones so ending a mission
    /// cannot remove ordinary quest triggers.
    /// </summary>
    internal void BeginMission(IEnumerable<SeasonZone> zones)
    {
        if (!Plugin.InRaid || !Plugin.SeasonalPlayer || Plugin.Player == null)
            throw new InvalidOperationException("Mission zones require an active seasonal raid.");

        EndMission();
        _player = Plugin.Player;
        var existing = Resources
            .FindObjectsOfTypeAll<TriggerWithId>()
            .AsValueEnumerable()
            .Where(static t => t && t.gameObject.scene.IsValid())
            .Select(t => t.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var zone in (zones ?? Array.Empty<SeasonZone>()).AsValueEnumerable())
        {
            if (zone == null || string.IsNullOrWhiteSpace(zone.Id))
                throw new InvalidOperationException("The mission descriptor contains an invalid zone.");
            if (!string.Equals(zone.Location, Location, StringComparison.Ordinal))
                throw new InvalidOperationException("Mission zone map does not match the active raid: " + zone.Id);
            if (_zones.ContainsKey(zone.Id) || existing.Contains(zone.Id))
                throw new InvalidOperationException("Mission zone ID collides with a native or campaign zone: " + zone.Id);

            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(zone.Scene);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Mission zone scene is unavailable: " + zone.Scene);
            var root = Volume(zone);
            try
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<NativeZoneBridge>().Initialize(zone);
                _zones.Add(zone.Id, root);
                _missionZoneIds.Add(zone.Id);
            }
            catch
            {
                if (root)
                    Destroy(root);
                throw;
            }
        }
    }

    internal void EndMission()
    {
        foreach (var id in _missionZoneIds.AsValueEnumerable().ToArray())
        {
            if (_zones.TryGetValue(id, out var zone) && zone)
            {
                zone.GetComponent<NativeZoneBridge>()?.Clear();
                Destroy(zone);
            }
            _zones.Remove(id);
        }
        _missionZoneIds.Clear();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        var player = Plugin.InRaid && Plugin.SeasonalPlayer ? Plugin.Player : null;
        if (player == _player)
        {
            return;
        }

        Clear();
        _player = player;
        if (!player)
        {
            return;
        }

        try
        {
            var existing = Resources
                .FindObjectsOfTypeAll<TriggerWithId>()
                .AsValueEnumerable()
                .Where(static t => t.gameObject.scene.IsValid())
                .Select(t => t.Id)
                .ToHashSet();
            foreach (
                var zone in Plugin.Current!.Zones.AsValueEnumerable().Where(z => z.Location == Location && ZoneLayoutRules.IsShared(z))
            )
            {
                if (existing.Contains(zone.Id))
                {
                    Plugin.LogInfo("Campaign zone ID collides with a native zone: " + zone.Id);
                    continue;
                }
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(zone.Scene);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Plugin.LogInfo("Campaign zone scene is unavailable: " + zone.Scene);
                    continue;
                }
                var root = Volume(zone);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<NativeZoneBridge>().Initialize(zone);
                _zones.Add(zone.Id, root);
            }
        }
        catch (Exception e)
        {
            Plugin.Error(e);
            Clear();
        }
    }

    internal static GameObject Volume(SeasonZone zone)
    {
        var root = new GameObject("Campaign zone " + zone.Id);
        root.transform.SetPositionAndRotation(Vector(zone.Position), Quaternion.Euler(Vector(zone.Rotation)));
        root.layer = LayerMask.NameToLayer("Triggers");
        if (zone.Shape == "Sphere")
        {
            var collider = root.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = zone.Radius;
        }
        else
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = Vector(zone.Size);
        }
        return root;
    }

    private void Clear()
    {
        foreach (var zone in _zones.Values.AsValueEnumerable().Where(static z => z))
        {
            zone.GetComponent<NativeZoneBridge>()?.Clear();
            Destroy(zone);
        }
        _zones.Clear();
        _missionZoneIds.Clear();
        _player = null;
    }

    private void OnDestroy()
    {
        Clear();
        if (Instance == this)
        {
            Instance = null;
        }
    }
}

// The bridge dispatches native enter/exit once per player, even when their body has several colliders.
public sealed class NativeZoneBridge : MonoBehaviour, IPhysicsTriggerWithStay
{
    public string Description
    {
        get { return "Campaign quest zone"; }
    }

    private static readonly List<NativeZoneBridge> Active = new();
    private readonly HashSet<Collider> _inside = new();
    private readonly List<TriggerWithId> _native = new();
    private Player? _owner;

    internal void Initialize(SeasonZone zone)
    {
        void Add<T>()
            where T : TriggerWithId
        {
            var child = new GameObject(typeof(T).Name);
            child.transform.SetParent(transform, false);
            var trigger = child.AddComponent<T>();
            trigger.SetId(zone.Id);
            _native.Add(trigger);
        }
        if (zone.Uses.Contains("VisitPlace"))
        {
            Add<ExperienceTrigger>();
        }
        else if (zone.Uses.Contains("InZone"))
        {
            Add<TriggerWithId>();
        }

        if (zone.Uses.Contains("LeaveItemAtLocation"))
        {
            Add<PlaceItemTrigger>();
        }
        if (zone.Uses.Contains("Salvage"))
        {
            Add<SalvageItemTrigger>();
            var salvage = zone.Salvage;
            ((SalvageItemTrigger)_native[_native.Count - 1]).Configure(
                salvage.RequiredItemTpl,
                salvage.SalvageTime,
                salvage
                    .Rewards.AsValueEnumerable()
                    .Select(r => new SalvageItemTrigger.SalvageReward
                    {
                        ItemTpl = r.ItemTpl,
                        Count = r.Count,
                        ToQuestInventory = r.ToQuestInventory,
                    })
                    .ToArray(),
                salvage.ConsumeRequiredItem
            );
            _ = PreloadSalvageRewards(salvage);
        }
    }

    private static async Task PreloadSalvageRewards(SalvageZoneSettings salvage)
    {
        try
        {
            var factory = Singleton<ItemFactory>.Instance;
            var pools = Singleton<ObjectsFactory>.Instance;
            if (factory == null || pools == null)
                return;
            var keys = new List<ResourceKey>();
            foreach (var reward in salvage.Rewards)
            {
                if (!factory.ItemTemplates.TryGetValue(reward.ItemTpl, out var template))
                    continue;
                if (template.Prefab != null)
                    keys.Add(template.Prefab);
                if (template.UsePrefab != null)
                    keys.Add(template.UsePrefab);
            }
            if (keys.Count > 0)
                await pools.LoadBundlesAndCreatePools(
                    0,
                    ObjectsFactory.AssemblyType.Local,
                    keys.ToArray(),
                    JobYieldPriority.Immediate,
                    null,
                    System.Threading.CancellationToken.None
                );
        }
        catch (Exception e)
        {
            Plugin.Error(e);
        }
    }

    public void OnTriggerEnter(Collider other)
    {
        if (!Singleton<GameWorld>.Instantiated)
        {
            return;
        }

        var player = Singleton<GameWorld>.Instance.GetPlayerByCollider(other);
        if (!player || player != Plugin.Player)
        {
            return;
        }

        foreach (var story in GetComponents<Story.StorySceneBinding>())
        {
            story.ZoneEnter(other);
        }

        if (!_inside.Add(other))
        {
            return;
        }

        if (_inside.Count != 1)
        {
            return;
        }

        _owner = player;
        Active.Add(this);
        foreach (var trigger in _native)
        {
            if (trigger)
                trigger.TriggerEnter(player);
        }
    }

    // EFT's physics dispatcher supplies two colliders; Unity messages only accept one.
    void IPhysicsTriggerWithStay.OnTriggerStay(Collider other, Collider trigger)
    {
        OnTriggerEnter(other);
    }

    private void OnTriggerStay(Collider other)
    {
        OnTriggerEnter(other);
    }

    public void OnTriggerExit(Collider other)
    {
        foreach (var story in GetComponents<Story.StorySceneBinding>())
        {
            story.ZoneExit(other);
        }

        if (_inside.Remove(other) && _inside.Count == 0)
        {
            Clear();
        }
    }

    internal void Clear()
    {
        Active.Remove(this);
        if (_owner)
        {
            foreach (var trigger in _native.AsValueEnumerable().Where(static t => t))
            {
                // Another overlapping placement volume may currently own the placement prompt.
                if (trigger is PlaceItemTrigger && _owner!.PlaceItemZone != trigger)
                {
                    continue;
                }

                trigger.TriggerExit(_owner!);
                if (trigger is PlaceItemTrigger)
                {
                    var remaining = Active
                        .AsValueEnumerable()
                        .Where(b => b && b._owner == _owner)
                        .SelectMany(b => b._native)
                        .OfType<PlaceItemTrigger>()
                        .LastOrDefault();
                    if (remaining)
                    {
                        remaining!.TriggerEnter(_owner!);
                    }
                }
            }
        }
        _inside.Clear();
        _owner = null;
    }

    private void OnDestroy()
    {
        Clear();
    }
}
