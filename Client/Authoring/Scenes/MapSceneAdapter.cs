using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Interactive;
using UnityEngine;
using UnityEngine.SceneManagement;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed partial class MapSceneAdapter : IDisposable
{
    private readonly SceneEditTransaction _transaction = new();
    private readonly List<GameObject> _ghosts = new();
    private Material? _ghostMaterial;
    private Material? _checkpointGhostMaterial;
    private Material? _exitGhostMaterial;
    internal readonly List<string> TargetErrors = new();

    internal static string ScaleRestriction(Transform target)
    {
        if (target.GetComponentInChildren<WorldInteractiveObject>(true))
            return "Props with native interactions retain their original size so grips, hinges and drawers stay aligned.";
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
            if (ScenePropSupport.OwnedComponent(c.GetType().FullName ?? ""))
            {
                if (copy)
                    return "This original can be moved, but copying its native interaction or marker is unsupported.";
                var restriction = ScenePropMovement.Restriction(t, c);
                if (restriction.Length > 0)
                    return restriction;
                continue;
            }
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
        var matches = FindTargetPath(target);
        if (matches.Count != 1)
            throw new InvalidOperationException("Missing or ambiguous target; rebind " + target.Path);
        var captured = Capture(matches[0], door);
        if (captured.Fingerprint != target.Fingerprint || captured.NativeId != target.NativeId)
            throw new InvalidOperationException("Scene target changed; rebind " + target.Path);
        return matches[0];
    }

    private static List<Transform> FindTargetPath(MapTarget target)
    {
        var matches = new List<Transform>();
        var foundScene = false;
        for (var index = 0; index < SceneManager.sceneCount; index++)
        {
            var scene = SceneManager.GetSceneAt(index);
            if (!scene.IsValid() || !scene.isLoaded || scene.name != target.Scene)
                continue;
            foundScene = true;
            VisitRoots(scene);
        }
        // EFT may keep geometry in the persistent scene, outside sceneCount.
        if (!foundScene)
            VisitRoots(LoadedScene(target.Scene));
        return matches;

        void VisitRoots(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                Visit(root.transform, "");
        }

        void Visit(Transform node, string parentPath)
        {
            var path = parentPath + node.name + "[" + node.GetSiblingIndex() + "]";
            if (string.Equals(path, target.Path, StringComparison.Ordinal))
                matches.Add(node);
            // Follow only matching ancestors, including inactive objects. Do not
            // split on '/' because Unity object names can contain that character.
            var prefix = path + "/";
            if (!target.Path.StartsWith(prefix, StringComparison.Ordinal))
                return;
            for (var child = 0; child < node.childCount; child++)
                Visit(node.GetChild(child), prefix);
        }
    }

    internal async Task ApplyAsync(MapLayout layout, bool requirePlayerRoute, CancellationToken token, bool runtime = false)
    {
        using var loading = UI.NativeLoadingStatus.Begin("Preparing authored scenery…");
        token.ThrowIfCancellationRequested();
        Reconcile(layout, runtime: runtime);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (Loading)
        {
            if (_disposed)
                throw new OperationCanceledException("The scene preview was closed.");
            if (timer.ElapsedMilliseconds > 60000)
                throw new TimeoutException("Item models did not finish loading within one minute.");
            await UniTask.Delay(25, delayType: DelayType.Realtime, cancellationToken: token);
        }
        token.ThrowIfCancellationRequested();
        if (_disposed)
            throw new OperationCanceledException("The scene preview was closed.");
        Apply(layout, requirePlayerRoute, runtime);
        await WaitForNavigationAsync(token);
    }

    internal static async Task WaitForNavigationAsync(CancellationToken token)
    {
        // Followers run in LateUpdate; carving becomes queryable the next frame.
        await UniTask.NextFrame(cancellationToken: token);
        await UniTask.NextFrame(cancellationToken: token);
    }

    internal void ApplyMission(MapLayout layout) => Apply(layout, true, true);

    internal void Apply(MapLayout layout, bool requirePlayerRoute = true, bool runtime = false)
    {
        Reconcile(layout, runtime: runtime);
        FlushVisuals();
        var errors = MapLayoutRules.Errors(layout, requirePlayerRoute);
        errors.AddRange(TargetErrors);
        if (Loading)
            errors.Add("Wait for item models to finish loading.");
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", errors));

        // Apply may be requested more than once while the walkthrough is being
        // prepared.  The previous transaction owns the old invisible volumes;
        // release them before creating this snapshot so blockers never stack.
        _transaction.Dispose();
        foreach (var barrier in layout.Barriers)
        {
            GameObject? go = null;
            SceneNavigation? navigation = null;
            _transaction.Apply(
                () =>
                {
                    go = Volume(barrier, false);
                    navigation = new SceneNavigation(go.transform, tacticalCover: false);
                },
                () =>
                {
                    navigation?.Dispose();
                    navigation = null;
                    if (go)
                        Remove(go!);
                    go = null;
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

    internal static string ContainerCopyRestriction(Transform source)
    {
        var native = NativeSupported(source);
        return native.Length > 0 ? native : SceneAssetCatalog.Restriction(source.gameObject, true);
    }

    internal static SceneAssetCatalog.Model LoadSceneCopy(MapTarget target)
    {
        var source = Resolve(target, false);
        if (target.Kind == "Prop") return new SceneAssetCatalog.Model { Object = CopyProp(source, true) };
        if (target.Kind != "Container") throw new InvalidOperationException("Unsupported scene copy.");
        var restriction = ContainerCopyRestriction(source);
        if (restriction.Length > 0) throw new InvalidOperationException(restriction);
        var wrapper = new GameObject("CampaignEditor container");
        wrapper.SetActive(false);
        try
        {
            var clone = UnityEngine.Object.Instantiate(source.gameObject, wrapper.transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = source.lossyScale;
            var container = clone.GetComponent<LootableContainer>();
            var displacement = container.OpenPosition - container.ClosedPosition;
            container.ClosedPosition = Vector3.zero;
            container.OpenPosition = displacement;
            container.Id = "wtt-preview-" + Guid.NewGuid().ToString("N");
            container.ItemOwner = null;
            container.IsInitialized = false;
            container.enabled = true;
            container.DoorState = EDoorState.Shut;
            clone.SetActive(true);
            return new SceneAssetCatalog.Model { Object = wrapper };
        }
        catch { Remove(wrapper); throw; }
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
                    // Native culling owns the source's enabled flag. The copy has
                    // no native culling owner to turn it back on after reload.
                    copy.enabled = true;
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
            // Reveal mesh ancestors on our detached copy only. Trigger culling
            // can deactivate source branches before the editor finishes discovery.
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                for (var node = renderer.transform; node; node = node.parent)
                    node.gameObject.SetActive(true);
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
                // Copies do not retain the source's cross-fade shader state.
                copy.fadeMode = LODFadeMode.None;
                copy.animateCrossFading = false;
                copy.enabled = true;
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

    private Material RouteGhostMaterial(bool exit)
    {
        ref var material = ref (exit ? ref _exitGhostMaterial : ref _checkpointGhostMaterial);
        if (!material)
        {
            material = new Material(GhostMaterial);
            var color = RouteOverlay.RoleColor(exit ? RouteRole.End : RouteRole.Checkpoint);
            color.a = .25f;
            material.color = color;
        }
        return material!;
    }

    private static Scene LoadedScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            throw new InvalidOperationException("Barrier has no captured scene.");

        // Normal raid scenes are reported by SceneManager.  Check the scene
        // handle rather than only its name so a map transition cannot receive
        // a collider in a scene that is still unloading.
        var named = SceneManager.GetSceneByName(sceneName);
        if (named.IsValid() && named.isLoaded)
            return named;
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded && scene.name == sceneName)
                return scene;
        }

        // EFT can keep raid geometry in the persistent scene, which Unity does
        // not always expose through sceneCount.  A live transform provides the
        // loaded Scene handle without creating or moving a placeholder object.
        foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (
                transform
                && transform.gameObject.scene.IsValid()
                && transform.gameObject.scene.isLoaded
                && transform.gameObject.scene.name == sceneName
            )
                return transform.gameObject.scene;
        }

        throw new InvalidOperationException("Barrier scene is not loaded: " + sceneName);
    }

    private GameObject Volume(MapVolume volume, bool ghost, Material? material = null)
    {
        GameObject? go = null;
        try
        {
            if (ghost)
            {
                // The editor representation is deliberately a render-only
                // primitive.  Disable before destroying the default collider so
                // it cannot participate in this frame's scene picking/physics.
                go = GameObject.CreatePrimitive(volume.Shape == "Sphere" ? PrimitiveType.Sphere : PrimitiveType.Cube);
                go.name = "CampaignEditor " + volume.Name;
                go.transform.SetPositionAndRotation(
                    ZoneRuntime.Vector(volume.Position),
                    Quaternion.Euler(ZoneRuntime.Vector(volume.Rotation))
                );
                go.transform.localScale = volume.Shape == "Sphere" ? Vector3.one * volume.Radius * 2 : ZoneRuntime.Vector(volume.Size);
                var collider = go.GetComponent<Collider>();
                if (collider)
                    collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
                go.layer = 2;
                go.GetComponent<Renderer>().sharedMaterial = material ? material : GhostMaterial;
                return go;
            }

            var lowPolyLayer = LayerMask.NameToLayer("LowPolyCollider");
            if (lowPolyLayer < 0)
                throw new InvalidOperationException("The LowPolyCollider layer is unavailable; barriers cannot be applied.");
            var scene = LoadedScene(volume.Scene);
            go = new GameObject("CampaignEditor " + volume.Name);
            go.layer = lowPolyLayer;
            go.transform.SetPositionAndRotation(ZoneRuntime.Vector(volume.Position), Quaternion.Euler(ZoneRuntime.Vector(volume.Rotation)));
            if (volume.Shape == "Sphere")
            {
                var collider = go.AddComponent<SphereCollider>();
                collider.isTrigger = false;
                collider.radius = volume.Radius;
            }
            else if (volume.Shape == "Box")
            {
                var collider = go.AddComponent<BoxCollider>();
                collider.isTrigger = false;
                collider.size = ZoneRuntime.Vector(volume.Size);
            }
            else
                throw new InvalidOperationException("Barrier shape is unsupported: " + volume.Shape);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
        catch
        {
            if (go)
                Remove(go!);
            throw;
        }
    }

    internal void Ghosts(MapLayout? layout)
    {
        ClearGhosts();

        if (layout == null)
            return;
        foreach (var volume in layout.Barriers)
            _ghosts.Add(Volume(volume, true));
        foreach (var volume in layout.Checkpoints)
            _ghosts.Add(Volume(volume, true, RouteGhostMaterial(false)));
        if (layout.Exit != null)
            _ghosts.Add(Volume(layout.Exit, true, RouteGhostMaterial(true)));
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
            if (_checkpointGhostMaterial)
                UnityEngine.Object.Destroy(_checkpointGhostMaterial);
            if (_exitGhostMaterial)
                UnityEngine.Object.Destroy(_exitGhostMaterial);
            _checkpointGhostMaterial = null;
            _exitGhostMaterial = null;
            Physics.SyncTransforms();
        }
    }
}
