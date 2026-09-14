using System.Security.Cryptography;
using System.Text;
using EFT;
using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal sealed partial class MapSceneAdapter : IDisposable
{
    private readonly SceneEditTransaction _transaction = new();
    private readonly List<GameObject> _ghosts = new();
    private Material? _ghostMaterial;
    internal readonly List<string> TargetErrors = new();

    internal static string ScaleRestriction(Transform target)
    {
        // Include disabled colliders: hiding a prop must not bypass its collision requirements.
        foreach (var collider in target.GetComponentsInChildren<MeshCollider>(true))
            if (collider.sharedMesh && !collider.sharedMesh.isReadable)
                return "This prop retains its original size because its collision mesh does not support resizing.";
        return "";
    }

    internal static string PathOf(Transform t) => (t.parent ? PathOf(t.parent) + "/" : "") + t.name + "[" + t.GetSiblingIndex() + "]";

    internal static string Supported(Transform? t, bool door = false, bool copy = false)
    {
        if (!t || !t!.gameObject.scene.IsValid() || !t.parent)
            return "Select a loaded scenery object, not a scene root.";
        if (
            t.GetComponentInParent<Player>()
            || t.GetComponentInParent<Canvas>()
            || t.name.StartsWith("CampaignEditor", StringComparison.Ordinal)
        )
            return "This object belongs to gameplay or editor infrastructure.";
        if (!door && (t.GetComponent<LootItem>() || t.GetComponent<LootableContainer>()))
            return NativeSupported(t);
        if (door)
            return t.GetComponent<Door>()?.GetType() == typeof(Door) ? "" : "Select a standard native door; special doors are unsupported.";
        // Reject aggregate map branches before allocating component arrays for the whole hierarchy.
        var pending = new Stack<Transform>();
        pending.Push(t);
        var nodes = 0;
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (++nodes > 256 || node.childCount > 64)
                return "Select an individual prop instead of an aggregate scene group.";
            for (var i = 0; i < node.childCount; i++)
                pending.Push(node.GetChild(i));
        }
        if (
            t.GetComponentsInParent<MonoBehaviour>(true)
                .AsValueEnumerable()
                .Any(c =>
                    c
                    && (
                        c is WorldInteractiveObject
                        || c.GetType().Name.Contains("Loot")
                        || c.GetType().Name.Contains("Quest")
                        || c.GetType().Name.Contains("Exfiltration")
                        || c.GetType().Name.Contains("TriggerWithId")
                    )
                )
        )
            return "This scenery belongs to a gameplay interaction or trigger.";
        if (!t.GetComponentsInChildren<MeshRenderer>(true).AsValueEnumerable().Any())
            return "Select a static mesh prop.";
        foreach (var c in t.GetComponentsInChildren<Component>(true))
        {
            if (!c)
                return "The prop contains a missing component.";
            if (
                c is Transform or MeshFilter or MeshRenderer or LODGroup or BoxCollider or SphereCollider or CapsuleCollider or MeshCollider
            )
                continue;
            if (c.GetType().FullName == "EFT.Ballistics.BallisticCollider")
                continue;
            if (ScenePropSupport.PreservedComponent(c.GetType().FullName ?? ""))
            {
                if (copy)
                    return "This original can be moved, rotated and resized, but copying " + c.GetType().Name + " is not supported.";
                continue;
            }
            return ScenePropSupport.Restriction(c.GetType().FullName ?? c.GetType().Name);
        }
        if (t.GetComponentsInChildren<Renderer>(true).AsValueEnumerable().Any(r => r.isPartOfStaticBatch))
            return "Combined static geometry cannot be moved safely. Choose an independent prop.";
        if (t.GetComponentsInChildren<MeshFilter>(true).AsValueEnumerable().Any(f => !f.sharedMesh))
            return "The prop mesh is unavailable.";
        var scale = t.lossyScale;
        if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0)
            return "Mirrored or zero-scale geometry is available for inspection only.";
        return "";
    }

    internal static MapTarget Capture(Transform target, bool door = false)
    {
        var error = Supported(target, door);
        if (error.Length > 0)
            throw new InvalidOperationException(error);
        if (!door && (target.GetComponent<LootItem>() || target.GetComponent<LootableContainer>()))
            return CaptureNative(target);
        var shape = target
            .GetComponentsInChildren<Component>(true)
            .AsValueEnumerable()
            .Select(c => c.GetType().FullName + ":" + c.name)
            .JoinToString("|");
        shape += "|" + target.position.ToString("R") + "|" + target.rotation.ToString("R") + "|" + target.lossyScale.ToString("R");
        shape +=
            "|"
            + target
                .GetComponentsInChildren<MeshFilter>(true)
                .AsValueEnumerable()
                .Select(f => f.sharedMesh.name + ":" + f.sharedMesh.vertexCount)
                .JoinToString("|");
        using var hash = SHA256.Create();
        return new MapTarget
        {
            Scene = target.gameObject.scene.name,
            Path = PathOf(target),
            Fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(shape))).Replace("-", ""),
            NativeId = target.GetComponent<Door>()?.Id ?? "",
        };
    }

    private static Transform Resolve(MapTarget target, bool door)
    {
        if (target.Kind != "Prop")
            return ResolveNative(target);
        var matches = Resources
            .FindObjectsOfTypeAll<Transform>()
            .AsValueEnumerable()
            .Where(t => t && t.gameObject.scene.IsValid() && t.gameObject.scene.name == target.Scene && PathOf(t) == target.Path)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Missing or ambiguous target; rebind " + target.Path);
        var captured = Capture(matches[0], door);
        if (captured.Fingerprint != target.Fingerprint || captured.NativeId != target.NativeId)
            throw new InvalidOperationException("Scene target changed; rebind " + target.Path);
        return matches[0];
    }

    internal void Apply(MapLayout layout)
    {
        Reconcile(layout);
        FlushVisuals();
        var errors = MapLayoutRules.Errors(layout, true);
        errors.AddRange(TargetErrors);
        if (Loading)
            errors.Add("Wait for item models to finish loading.");
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", errors));
        foreach (var barrier in layout.Barriers)
        {
            var go = Volume(barrier, false);
            _transaction.Apply(
                () => { },
                () =>
                {
                    if (go)
                        Remove(go);
                }
            );
        }
        ClearGhosts();
        Physics.SyncTransforms();
    }

    private static void Pose(Transform t, MapObjectEdit edit, bool scale)
    {
        t.SetPositionAndRotation(ZoneRuntime.Vector(edit.Position), Quaternion.Euler(ZoneRuntime.Vector(edit.Rotation)));
        if (scale)
            t.localScale = ZoneRuntime.Vector(edit.Scale);
    }

    private static GameObject CopyProp(Transform source, bool collision)
    {
        var error = Supported(source, copy: true);
        if (error.Length > 0)
            throw new InvalidOperationException(error);
        var root = new GameObject("CampaignEditor prop");
        try
        {
            var copies = new Dictionary<Transform, Transform>();
            void Copy(Transform from, Transform to)
            {
                copies[from] = to;
                to.gameObject.layer = from.gameObject.layer;
                var mesh = from.GetComponent<MeshFilter>();
                var renderer = from.GetComponent<MeshRenderer>();
                if (mesh && renderer)
                {
                    to.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                    var copy = to.gameObject.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = renderer.sharedMaterials;
                    copy.enabled = renderer.enabled;
                    copy.shadowCastingMode = renderer.shadowCastingMode;
                    copy.receiveShadows = renderer.receiveShadows;
                }
                if (collision)
                    foreach (var collider in from.GetComponents<Collider>())
                    {
                        if (collider.isTrigger || !collider.enabled)
                            continue;
                        if (collider is BoxCollider b)
                        {
                            var c = to.gameObject.AddComponent<BoxCollider>();
                            c.center = b.center;
                            c.size = b.size;
                        }
                        else if (collider is SphereCollider s)
                        {
                            var c = to.gameObject.AddComponent<SphereCollider>();
                            c.center = s.center;
                            c.radius = s.radius;
                        }
                        else if (collider is CapsuleCollider cap)
                        {
                            var c = to.gameObject.AddComponent<CapsuleCollider>();
                            c.center = cap.center;
                            c.radius = cap.radius;
                            c.height = cap.height;
                            c.direction = cap.direction;
                        }
                        else if (collider is MeshCollider m)
                        {
                            var c = to.gameObject.AddComponent<MeshCollider>();
                            c.sharedMesh = m.sharedMesh;
                            c.convex = m.convex;
                        }
                    }
                foreach (Transform child in from)
                {
                    var next = new GameObject(child.name).transform;
                    next.SetParent(to, false);
                    next.localPosition = child.localPosition;
                    next.localRotation = child.localRotation;
                    next.localScale = child.localScale;
                    Copy(child, next);
                    next.gameObject.SetActive(child.gameObject.activeSelf);
                }
            }
            Copy(source, root.transform);
            foreach (var group in source.GetComponentsInChildren<LODGroup>(true))
            {
                var copy = copies[group.transform].gameObject.AddComponent<LODGroup>();
                var lods = group.GetLODs();
                for (var i = 0; i < lods.Length; i++)
                    lods[i].renderers = lods[i]
                        .renderers.AsValueEnumerable()
                        .Where(r => r && copies.ContainsKey(r.transform))
                        .Select(r => copies[r.transform].GetComponent<Renderer>())
                        .ToArray();
                copy.SetLODs(lods);
                copy.localReferencePoint = group.localReferencePoint;
                copy.size = group.size;
                copy.fadeMode = group.fadeMode;
                copy.animateCrossFading = group.animateCrossFading;
                copy.enabled = group.enabled;
            }
            root.transform.SetPositionAndRotation(source.position, source.rotation);
            root.transform.localScale = source.lossyScale;
            return root;
        }
        catch
        {
            Remove(root);
            throw;
        }
    }

    private Material GhostMaterial
    {
        get
        {
            if (!_ghostMaterial)
            {
                _ghostMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                _ghostMaterial.SetInt("_SrcBlend", 5);
                _ghostMaterial.SetInt("_DstBlend", 10);
                _ghostMaterial.SetInt("_ZWrite", 0);
                _ghostMaterial.color = new Color(.3f, .8f, 1f, .25f);
            }
            return _ghostMaterial!;
        }
    }

    private GameObject Volume(MapVolume volume, bool ghost)
    {
        var go = GameObject.CreatePrimitive(volume.Shape == "Sphere" ? PrimitiveType.Sphere : PrimitiveType.Cube);
        go.name = "CampaignEditor " + volume.Name;
        go.transform.SetPositionAndRotation(ZoneRuntime.Vector(volume.Position), Quaternion.Euler(ZoneRuntime.Vector(volume.Rotation)));
        go.transform.localScale = volume.Shape == "Sphere" ? Vector3.one * volume.Radius * 2 : ZoneRuntime.Vector(volume.Size);
        if (ghost)
        {
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.layer = 2;
            go.GetComponent<Renderer>().sharedMaterial = GhostMaterial;
        }
        return go;
    }

    internal void Ghosts(MapLayout? layout)
    {
        ClearGhosts();

        if (layout == null)
            return;
        foreach (
            var volume in layout
                .Barriers.AsValueEnumerable()
                .Concat(layout.Checkpoints)
                .Concat(layout.Exit == null ? Array.Empty<MapVolume>() : new[] { layout.Exit })
        )
            _ghosts.Add(Volume(volume, true));
    }

    private static void Remove(GameObject value)
    {
        value.SetActive(false);
        UnityEngine.Object.Destroy(value);
    }

    internal void ClearGhosts()
    {
        foreach (var go in _ghosts)
            if (go)
                Remove(go!);
        _ghosts.Clear();
    }

    public void Dispose()
    {
        try
        {
            try
            {
                DisposeEdits();
            }
            finally
            {
                _transaction.Dispose();
            }
        }
        finally
        {
            ClearGhosts();
            if (_ghostMaterial)
                UnityEngine.Object.Destroy(_ghostMaterial);
            _ghostMaterial = null;
            Physics.SyncTransforms();
        }
    }
}
