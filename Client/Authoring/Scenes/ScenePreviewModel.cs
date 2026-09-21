using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Thumbnails need geometry, not permission to reproduce an object's gameplay components.
internal static class ScenePreviewModel
{
    internal static GameObject Copy(Transform source)
    {
        var root = new GameObject("CampaignEditor visual preview");
        root.SetActive(false);
        try
        {
            var excluded = new HashSet<Renderer>();
            foreach (var group in source.GetComponentsInChildren<LODGroup>(true))
            {
                // Only one mesh LOD belongs in a still image; never overlay impostors.
                var lods = group.GetLODs();
                var chosen = -1;
                for (var i = 0; i < lods.Length && chosen < 0; i++)
                    foreach (var renderer in lods[i].renderers)
                        if (Renderable(renderer))
                        {
                            chosen = i;
                            break;
                        }
                for (var i = 0; i < lods.Length; i++)
                    foreach (var renderer in lods[i].renderers)
                        if (renderer)
                            excluded.Add(renderer);
                if (chosen >= 0)
                    foreach (var renderer in lods[chosen].renderers)
                        excluded.Remove(renderer);
            }
            var copies = new Dictionary<Transform, Transform> { [source] = root.transform };
            Transform Node(Transform from)
            {
                if (copies.TryGetValue(from, out var copy))
                    return copy;
                var parent = Node(from.parent);
                copy = new GameObject(from.name).transform;
                copy.SetParent(parent, false);
                copy.localPosition = from.localPosition;
                copy.localRotation = from.localRotation;
                copy.localScale = from.localScale;
                copies.Add(from, copy);
                return copy;
            }
            var count = 0;
            foreach (var renderer in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!Renderable(renderer) || excluded.Contains(renderer))
                    continue;
                var node = Node(renderer.transform);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                node.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                count++;
            }
            if (count == 0)
                throw new InvalidOperationException("No independent mesh is available for a preview.");
            // Scene roots often contain the model's import-axis correction. Keep it,
            // along with child transforms, even when native culling has hidden the source.
            root.transform.rotation = source.rotation;
            root.transform.localScale = source.lossyScale;
            return root;
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    private static bool Renderable(Renderer renderer) =>
        renderer
        && renderer is MeshRenderer
        && !renderer.isPartOfStaticBatch
        && renderer.GetComponent<MeshFilter>() is { } mesh
        && mesh.sharedMesh;
}
