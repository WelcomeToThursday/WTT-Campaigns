using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace WTT.Campaigns.Client.Authoring;

// Owned by the scene edit, never by the native prop. Separate roots preserve
// native hierarchy fingerprints and let undo remove only our navigation cuts.
internal sealed class SceneNavigation : IDisposable
{
    private static readonly HashSet<SceneNavigation> Active = new();
    private readonly List<GameObject> _proxies = new();

    internal SceneNavigation(Transform root)
    {
        try
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.isTrigger)
                    continue;
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
                follower.Sync();
                proxy.SetActive(true);
            }
            Active.Add(this);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
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
}

internal sealed class SceneNavigationFollower : MonoBehaviour
{
    internal Collider Source = null!;
    internal NavMeshObstacle Obstacle = null!;

    internal void Sync()
    {
        Obstacle.enabled = Source && Source.enabled && !Source.isTrigger && Source.gameObject.activeInHierarchy;
        if (!Source)
            return;
        transform.SetPositionAndRotation(Source.transform.position, Source.transform.rotation);
        transform.localScale = Source.transform.lossyScale;
    }

    private void LateUpdate() => Sync();
}
