using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class SceneBounds
{
    // Generated props retain their native rotation below an authored placement root.
    // Use that geometry's axes for the outline without changing the saved root pose.
    internal static Transform? SelectionFrame(Transform? target, Transform? geometry) =>
        target && geometry && geometry!.IsChildOf(target) ? geometry : target;

    internal static bool TryGet(Transform? target, out Bounds bounds) => TryGet(target, out bounds, out _);

    internal static bool TryGet(Transform? target, out Bounds bounds, out Bounds localBounds)
    {
        bounds = default;
        localBounds = default;
        if (!target)
            return false;
        var found = false;
        foreach (var renderer in target!.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;
            var b = renderer.bounds;
            if (
                !float.IsFinite(b.center.x)
                || !float.IsFinite(b.center.y)
                || !float.IsFinite(b.center.z)
                || !float.IsFinite(b.size.sqrMagnitude)
                || b.size.sqrMagnitude < .00000001f
            )
                continue;
            if (found)
                bounds.Encapsulate(b);
            else
                bounds = b;
            // Start with renderer-local bounds, not the inflated world AABB. The
            // full matrices retain nested rotation, nonuniform scale and shear.
            Encapsulate(ref localBounds, renderer.localBounds, target.worldToLocalMatrix * renderer.localToWorldMatrix, found);
            found = true;
        }
        if (!found)
            foreach (var collider in target.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger)
                    continue;
                if (found)
                    bounds.Encapsulate(collider.bounds);
                else
                    bounds = collider.bounds;
                if (collider is BoxCollider box)
                    Encapsulate(
                        ref localBounds,
                        new Bounds(box.center, box.size),
                        target.worldToLocalMatrix * box.transform.localToWorldMatrix,
                        found
                    );
                else if (collider is MeshCollider mesh && mesh.sharedMesh)
                    Encapsulate(
                        ref localBounds,
                        mesh.sharedMesh.bounds,
                        target.worldToLocalMatrix * mesh.transform.localToWorldMatrix,
                        found
                    );
                else
                    Encapsulate(ref localBounds, collider.bounds, target.worldToLocalMatrix, found);
                found = true;
            }
        return found;
    }

    internal static Vector3 Corner(Bounds bounds, int index) =>
        bounds.center
        + Vector3.Scale(bounds.extents, new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1));

    private static void Encapsulate(ref Bounds result, Bounds source, Matrix4x4 matrix, bool found)
    {
        for (var i = 0; i < 8; i++)
        {
            var corner = matrix.MultiplyPoint3x4(Corner(source, i));
            if (!found && i == 0)
                result = new Bounds(corner, Vector3.zero);
            else
                result.Encapsulate(corner);
        }
    }
}
