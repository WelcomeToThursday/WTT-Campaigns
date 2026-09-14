using System;
using System.Collections.Generic;
using UnityEngine;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring;

internal static class ScenePicking
{
    // Maps and Scene share world selection. Other categories keep their capture/zone tools.
    internal static bool Dispatch(bool editor, string mode, Action pick)
    {
        if (!editor || (mode != "Maps" && mode != "Scene"))
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
        // Renderer-only scenery has no physics hit. Bounds offer inspection without adding
        // colliders or changing the original map; an existing nearer solid hit still wins.
        foreach (var renderer in renderers)
        {
            if (
                !renderer
                || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                || !renderer.enabled
                || !renderer.gameObject.activeInHierarchy
                || renderer.bounds.Contains(ray.origin)
                || !renderer.bounds.IntersectRay(ray, out var entry)
                || entry >= distance
                || !Inspectable(renderer.transform, authored)
            )
                continue;
            if (selected && (renderer.transform == selected || renderer.transform.IsChildOf(selected)))
                continue;
            selected = renderer.transform;
            distance = entry;
        }
        return selected;
    }
}
