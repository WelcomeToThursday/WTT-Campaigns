using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CampaignsContainerLibraryCheck
{
    public static void Run()
    {
        var path = Environment.GetEnvironmentVariable("WTT_CONTAINER_BUNDLE");
        var bundle = AssetBundle.LoadFromFile(path);
        if (!bundle)
            throw new Exception("Container bundle failed to load");
        int count = 0;
        foreach (var name in bundle.GetAllAssetNames())
        {
            Debug.Log("CHECKING CONTAINER " + name);
            var prefab = bundle.LoadAsset<GameObject>(name);
            if (!prefab)
                throw new Exception("Missing prefab " + name);
            var components = prefab.GetComponentsInChildren<Component>(true);
            // Native EFT scripts are resolved by the game, not the SDK editor.
            var containers = components.Where(c => c && c.GetType().FullName == "EFT.Interactive.LootableContainer").ToArray();

            if (!prefab.GetComponentsInChildren<MeshRenderer>(true).Any())
                throw new Exception("No model " + name);
            if (prefab.GetComponentsInChildren<MeshFilter>(true).Any(m => !m.sharedMesh))
                throw new Exception("Missing mesh " + name);
            if (
                prefab
                    .GetComponentsInChildren<Renderer>(true)
                    .Any(r => r.enabled && Active(r.transform) && r.sharedMaterials.Any(m => !m || !m.shader))
            )
                throw new Exception("Missing material " + name);
            Debug.Log("CONTAINER VERIFIED " + name + " " + prefab.name);
            count++;
        }
        bundle.Unload(true);
        File.WriteAllText(path + ".verified", count.ToString());
        Debug.Log("CONTAINER LIBRARY VERIFIED " + count);
    }

    private static bool Active(Transform node)
    {
        for (var current = node; current; current = current.parent)
            if (!current.gameObject.activeSelf)
                return false;
        return true;
    }
}
