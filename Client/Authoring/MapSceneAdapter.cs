using System.Security.Cryptography;
using System.Text;
using EFT;
using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal sealed class MapSceneAdapter : IDisposable
{
    private readonly SceneEditTransaction _transaction = new();
    private readonly List<GameObject> _ghosts = new();
    private Material? _ghostMaterial;
    internal readonly List<string> TargetErrors = new();

    internal static string PathOf(Transform t) => (t.parent ? PathOf(t.parent) + "/" : "") + t.name + "[" + t.GetSiblingIndex() + "]";

    internal static string Supported(Transform? t, bool door = false)
    {
        if (!t || !t!.gameObject.scene.IsValid() || !t.parent)
            return "Select a loaded scenery object, not a scene root.";
        if (
            t.GetComponentInParent<Player>()
            || t.GetComponentInParent<Canvas>()
            || t.name.StartsWith("CampaignEditor", StringComparison.Ordinal)
        )
            return "This object belongs to gameplay or editor infrastructure.";
        if (door)
            return t.GetComponent<Door>()?.GetType() == typeof(Door) ? "" : "Select a standard native door; special doors are unsupported.";
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
            if (c is Transform or MeshFilter or MeshRenderer or BoxCollider or SphereCollider or CapsuleCollider or MeshCollider)
                continue;
            if (c.GetType().FullName == "EFT.Ballistics.BallisticCollider")
                continue;
            return "Unsupported scenery component: " + c.GetType().Name;
        }
        if (t.GetComponentsInChildren<Renderer>(true).AsValueEnumerable().Any(r => r.isPartOfStaticBatch))
            return "Combined static geometry cannot be moved safely. Choose an independent prop.";
        if (t.GetComponentsInChildren<MeshFilter>(true).AsValueEnumerable().Any(f => !f.sharedMesh))
            return "The prop mesh is unavailable.";
        return "";
    }

    internal static MapTarget Capture(Transform target, bool door = false)
    {
        var error = Supported(target, door);
        if (error.Length > 0)
            throw new InvalidOperationException(error);
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
        if (!EditorMode.Ready || layout.Location != ZoneRuntime.Location)
            throw new InvalidOperationException("Open this layout's map in Editor mode.");
        var errors = MapLayoutRules.Errors(layout, true);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", errors));
        // Resolve everything before touching the scene.
        var objects = layout.Objects.AsValueEnumerable().Select(e => (Edit: e, Target: Resolve(e.Target, false))).ToArray();
        var doors = layout.Doors.AsValueEnumerable().Select(e => (Edit: e, Target: Resolve(e.Target, true).GetComponent<Door>())).ToArray();
        try
        {
            foreach (var pair in objects)
            {
                var target = pair.Target;
                if (pair.Edit.Operation == "Copy")
                {
                    GameObject? copy = null;
                    _transaction.Apply(
                        () =>
                        {
                            copy = CopyProp(target, true);
                            Pose(copy.transform, pair.Edit, true);
                        },
                        () =>
                        {
                            if (copy)
                                Remove(copy!);
                        }
                    );
                    continue;
                }
                var position = target.position;
                var rotation = target.rotation;
                var active = target.gameObject.activeSelf;
                _transaction.Apply(
                    () =>
                    {
                        if (pair.Edit.Operation == "Hide")
                            target.gameObject.SetActive(false);
                        else
                            Pose(target, pair.Edit, false);
                    },
                    () =>
                    {
                        if (target)
                        {
                            target.SetPositionAndRotation(position, rotation);
                            target.gameObject.SetActive(active);
                        }
                    }
                );
            }
            foreach (var pair in doors)
            {
                if (pair.Edit.State == "Unchanged")
                    continue;
                var door = pair.Target;
                var state = door.DoorState;
                var angle = door.CurrentAngle;
                var next = (EDoorState)Enum.Parse(typeof(EDoorState), pair.Edit.State);
                _transaction.Apply(
                    () =>
                        door.SetInitialSyncState(
                            new WorldInteractiveObject.InteractiveObjectStatusInfo(door.Id, next, door.GetAngle(next))
                        ),
                    () =>
                    {
                        if (door)
                        {
                            door.SetInitialSyncState(new WorldInteractiveObject.InteractiveObjectStatusInfo(door.Id, state, angle));
                            door.CurrentAngle = angle;
                        }
                    }
                );
            }
            foreach (var barrier in layout.Barriers)
            {
                GameObject? go = null;
                _transaction.Apply(
                    () =>
                    {
                        go = Volume(barrier, false);
                    },
                    () =>
                    {
                        if (go)
                            Remove(go!);
                    }
                );
            }
            Physics.SyncTransforms();
        }
        catch
        {
            _transaction.Dispose();
            throw;
        }
    }

    private static void Pose(Transform t, MapObjectEdit edit, bool scale)
    {
        t.SetPositionAndRotation(ZoneRuntime.Vector(edit.Position), Quaternion.Euler(ZoneRuntime.Vector(edit.Rotation)));
        if (scale)
            t.localScale = ZoneRuntime.Vector(edit.Scale);
    }

    private static GameObject CopyProp(Transform source, bool collision)
    {
        var root = new GameObject("CampaignEditor prop");
        try
        {
            void Copy(Transform from, Transform to)
            {
                to.gameObject.layer = from.gameObject.layer;
                var mesh = from.GetComponent<MeshFilter>();
                var renderer = from.GetComponent<MeshRenderer>();
                if (mesh && renderer)
                {
                    to.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                    to.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
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
        TargetErrors.Clear();
        if (layout == null)
            return;
        foreach (
            var volume in layout
                .Barriers.AsValueEnumerable()
                .Concat(layout.Checkpoints)
                .Concat(layout.Exit == null ? Array.Empty<MapVolume>() : new[] { layout.Exit })
        )
            _ghosts.Add(Volume(volume, true));
        CheckDoors(layout);
        foreach (var edit in layout.Objects)
        {
            try
            {
                var copy = CopyProp(Resolve(edit.Target, false), false);
                _ghosts.Add(copy);
                if (edit.Operation != "Hide")
                    Pose(copy.transform, edit, edit.Operation == "Copy");
                foreach (var renderer in copy.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.AsValueEnumerable().Select(_ => GhostMaterial).ToArray();
                foreach (var t in copy.GetComponentsInChildren<Transform>())
                    t.gameObject.layer = 2;
            }
            catch (InvalidOperationException e)
            {
                TargetErrors.Add(e.Message);
            }
        }
    }

    internal void CheckDoors(MapLayout layout)
    {
        foreach (var door in layout.Doors)
            try
            {
                Resolve(door.Target, true);
            }
            catch (InvalidOperationException e)
            {
                TargetErrors.Add(e.Message);
            }
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
            _transaction.Dispose();
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
