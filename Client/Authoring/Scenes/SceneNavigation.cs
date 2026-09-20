using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Owned by the scene edit, never by the native prop. Separate roots preserve
// native hierarchy fingerprints and let undo remove only our navigation cuts.
internal sealed class SceneNavigation : IDisposable
{
    internal static long Revision { get; private set; }
    internal static int ReadyFrame { get; private set; }

    internal static void Changed()
    {
        Revision++;
        ReadyFrame = Time.frameCount + 2;
    }

    private static readonly HashSet<SceneNavigation> Active = new();
    private static Dictionary<Collider, float>? _paintSupportCaps;
    private static object? _paintSupportOwner;

    // A physical top face used by an explicit Add brush must not be erased by
    // its own authored-object box cut. Keep the solid below that face carved.
    internal static void SetPaintSupportCaps(object preview, Dictionary<Collider, float>? caps)
    {
        // Cancellation from a closed editor may settle after a new preview starts.
        if (caps == null && !ReferenceEquals(preview, _paintSupportOwner))
            return;
        _paintSupportOwner = caps == null ? null : preview;
        _paintSupportCaps = caps;
        foreach (var owner in Active)
        foreach (var proxy in owner._proxies)
            if (proxy && proxy.GetComponent<SceneNavigationFollower>() is { } follower)
                follower.Sync();
    }

    internal static bool PaintSupportCap(Collider collider, out float top)
    {
        top = 0;
        return _paintSupportCaps?.TryGetValue(collider, out top) == true;
    }

    private readonly List<GameObject> _proxies = new();
    private readonly List<Collider> _coverColliders = new();

    internal SceneNavigation(Transform root, bool tacticalCover = true)
    {
        try
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger)
                    continue;
                if (tacticalCover)
                    _coverColliders.Add(collider);
                Bounds bounds;
                switch (collider)
                {
                    case BoxCollider box:
                        bounds = new Bounds(box.center, box.size);
                        break;
                    case SphereCollider sphere:
                        bounds = new Bounds(sphere.center, Vector3.one * sphere.radius * 2);
                        break;
                    case CapsuleCollider capsule:
                        var size = Vector3.one * capsule.radius * 2;
                        size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2);
                        bounds = new Bounds(capsule.center, size);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh:
                        // Mesh bounds are available even for non-readable native meshes.
                        // Keep each collider separate instead of sealing an entire prop hierarchy.
                        bounds = mesh.sharedMesh.bounds;
                        break;
                    default:
                        continue;
                }
                if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                    continue;
                var proxy = new GameObject("CampaignEditor navigation");
                _proxies.Add(proxy);
                proxy.SetActive(false);
                SceneManager.MoveGameObjectToScene(proxy, root.gameObject.scene);
                var obstacle = proxy.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = bounds.center;
                obstacle.size = bounds.size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                obstacle.carvingMoveThreshold = .01f;
                var follower = proxy.AddComponent<SceneNavigationFollower>();
                follower.Source = collider;
                follower.Obstacle = obstacle;
                follower.OriginalCenter = bounds.center;
                follower.OriginalSize = bounds.size;
                follower.Sync();
                proxy.SetActive(true);
            }
            Active.Add(this);
            if (_proxies.Count > 0)
                Changed();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_proxies.Count > 0)
            Changed();
        Active.Remove(this);
        foreach (var proxy in _proxies)
            if (proxy)
            {
                proxy.SetActive(false);
                UnityEngine.Object.Destroy(proxy);
            }
        _proxies.Clear();
    }

    internal static void VisitObstacles(Action<NavMeshObstacle> visit)
    {
        foreach (var owner in Active)
        foreach (var proxy in owner._proxies)
            if (proxy && proxy.activeInHierarchy && proxy.GetComponent<NavMeshObstacle>() is { } obstacle && obstacle.enabled)
                visit(obstacle);
    }

    internal static void VisitCoverColliders(Action<Collider> visit)
    {
        foreach (var owner in Active)
        foreach (var collider in owner._coverColliders)
            if (collider && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy)
                visit(collider);
    }
}

internal sealed class SceneNavigationFollower : MonoBehaviour
{
    internal Collider Source = null!;
    internal NavMeshObstacle Obstacle = null!;
    internal Vector3 OriginalCenter,
        OriginalSize;

    internal void Sync()
    {
        var size = OriginalSize;
        var center = OriginalCenter;
        if (Source && SceneNavigation.PaintSupportCap(Source, out var top))
        {
            var bottom = center.y - size.y * .5f;
            size.y = Mathf.Clamp(top - bottom, .01f, size.y);
            center.y = bottom + size.y * .5f;
        }
        var enabled = Source && Source.enabled && !Source.isTrigger && Source.gameObject.activeInHierarchy;
        var changed =
            Obstacle.enabled != enabled
            || Obstacle.size != size
            || Obstacle.center != center
            || (
                Source
                && (
                    transform.position != Source.transform.position
                    || transform.rotation != Source.transform.rotation
                    || transform.localScale != Source.transform.lossyScale
                )
            );
        Obstacle.enabled = enabled;
        Obstacle.size = size;
        Obstacle.center = center;
        if (changed)
            SceneNavigation.Changed();
        if (!Source)
            return;
        transform.SetPositionAndRotation(Source.transform.position, Source.transform.rotation);
        transform.localScale = Source.transform.lossyScale;
    }

    private void LateUpdate() => Sync();
}
