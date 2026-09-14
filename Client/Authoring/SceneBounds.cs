using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

internal static class SceneBounds
{
    internal static bool TryGet(Transform? target, out Bounds bounds)
    {
        bounds = default;
        if (!target) return false;
        var found = false;
        foreach (var renderer in target!.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
            var b = renderer.bounds;
            if (!float.IsFinite(b.center.x) || !float.IsFinite(b.center.y) || !float.IsFinite(b.center.z)
                || !float.IsFinite(b.size.sqrMagnitude) || b.size.sqrMagnitude < .00000001f) continue;
            if (found) bounds.Encapsulate(b); else bounds = b;
            found = true;
        }
        if (!found)
            foreach (var collider in target.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (found) bounds.Encapsulate(collider.bounds); else bounds = collider.bounds;
                found = true;
            }
        return found;
    }
}
