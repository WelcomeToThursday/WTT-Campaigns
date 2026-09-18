using System;
using System.Collections.Generic;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class ScenePicking
{
    // The viewport is shared by all editor tools; its selection is not tab-gated.
    internal static bool Dispatch(bool editor, string mode, Action pick)
    {
        if (!editor)
            return false;
        pick();
        return true;
    }

    private static bool Inspectable(Transform target, Func<Transform, bool>? authored)
    {
        if (authored?.Invoke(target) == true)
            return true;
        for (var t = target; t; t = t.parent)
        {
            if (
                t.GetComponent<Canvas>()
                || t.name.StartsWith("CampaignEditor", StringComparison.Ordinal)
                || t.name.StartsWith("SeasonalRaidEditor", StringComparison.Ordinal)
            )
                return false;
            foreach (var component in t.GetComponents<MonoBehaviour>())
                if (component)
                    for (var type = component.GetType(); type != null; type = type.BaseType)
                        if (type.FullName == "EFT.Player")
                            return false;
        }
        return target.gameObject.scene.IsValid();
    }

    internal static Transform? Pick(Ray ray, IEnumerable<Renderer> renderers, Func<Transform, bool>? authored = null)
    {
        Physics.SyncTransforms();
        var hits = Physics.RaycastAll(ray, 1000, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Transform? selected = null;
        var distance = 1000f;
        foreach (var hit in hits)
        {
            if (!Inspectable(hit.transform, authored))
                continue;
            selected = hit.transform;
            distance = hit.distance;
            break;
        }
        // Bounds only reject candidates; empty space in a rotated object's world AABB
        // must never replace a real hit on the object under the cursor.
        foreach (var renderer in renderers)
        {
            if (
                !renderer
                || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy
                || !renderer.bounds.IntersectRay(ray, out var entry)
                || entry >= distance
                || !Inspectable(renderer.transform, authored)
            )
                continue;
            if (selected && (renderer.transform == selected || renderer.transform.IsChildOf(selected)))
                continue;
            if (MeshHit(renderer, ray, ref distance))
                selected = renderer.transform;
        }
        return selected;
    }

    private static bool MeshHit(Renderer renderer, Ray ray, ref float distance)
    {
        Mesh? baked = null;
        try
        {
            Mesh? mesh;
            if (renderer is SkinnedMeshRenderer skin)
            {
                if (!skin.sharedMesh)
                    return false;
                baked = new Mesh();
                skin.BakeMesh(baked);
                mesh = baked;
            }
            else
            {
                // Batched vertices no longer use this renderer's local coordinate frame.
                // Unreadable native meshes retain their physics hit, never an AABB hit.
                if (renderer.isPartOfStaticBatch)
                    return false;
                mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            }
            if (!mesh || !mesh!.isReadable)
                return false;
            var inverse = renderer.transform.worldToLocalMatrix;
            var origin = Numeric(inverse.MultiplyPoint3x4(ray.origin));
            var direction = Numeric(inverse.MultiplyVector(ray.direction));
            var vertices = mesh.vertices;
            var hit = false;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    continue;
                var indices = mesh.GetTriangles(submesh);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                    hit |= ScenePickGeometry.Triangle(
                        origin, direction,
                        Numeric(vertices[indices[i]]), Numeric(vertices[indices[i + 1]]), Numeric(vertices[indices[i + 2]]),
                        ref distance
                    );
            }
            return hit;
        }
        finally
        {
            if (baked)
                UnityEngine.Object.Destroy(baked);
        }
    }

    private static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.x, value.y, value.z);
}
