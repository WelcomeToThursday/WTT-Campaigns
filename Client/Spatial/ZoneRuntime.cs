using Comfort.Common;
using EFT;
using EFT.Interactive;
using SeasonalPerks.Shared.Spatial;
using UnityEngine;

namespace SeasonalPerks.Client.Spatial;

public sealed class ZoneRuntime : MonoBehaviour
{
    internal static ZoneRuntime? Instance;
    private Player? _player;
    private readonly Dictionary<string, GameObject> _zones = new();
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
                .Where(t => t.gameObject.scene.IsValid())
                .Select(t => t.Id)
                .ToHashSet();
            foreach (var zone in Plugin.Current!.Zones.Where(z => z.Location == Location))
            {
                if (existing.Contains(zone.Id))
                {
                    Plugin.LogInfo("Seasonal zone ID collides with a native zone: " + zone.Id);
                    continue;
                }
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(zone.Scene);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Plugin.LogInfo("Seasonal zone scene is unavailable: " + zone.Scene);
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
        var root = new GameObject("Seasonal zone " + zone.Id);
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
        foreach (var zone in _zones.Values.Where(z => z))
        {
            zone.GetComponent<NativeZoneBridge>()?.Clear();
            Destroy(zone);
        }
        _zones.Clear();
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
        get { return "Seasonal quest zone"; }
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
            trigger.TriggerEnter(player);
        }
    }

    public void OnTriggerStay(Collider other, Collider trigger)
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
            foreach (var trigger in _native.Where(t => t))
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
                        .Where(b => b && b._owner == _owner)
                        .SelectMany(b => b._native)
                        .OfType<PlaceItemTrigger>()
                        .LastOrDefault();
                    if (remaining)
                    {
                        remaining.TriggerEnter(_owner!);
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
