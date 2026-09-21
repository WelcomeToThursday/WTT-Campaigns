using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Asset validation in the SDK only. Never opens a playable scene or starts the game.
public static class CampaignsLevelPropValidator
{
    [Serializable]
    private class Input
    {
        public string directory;
        public string clientAssembly;
        public string[] bundles;
        public Entry[] assets;
    }

    [Serializable]
    private class Entry
    {
        public string bundle;
        public string asset;
        public Quaternion rotation;
        public Vector3 scale;
    }

    public static void Validate()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "-levelPropValidation");
        if (index < 0 || index + 1 == args.Length)
            throw new InvalidOperationException("Missing level prop validation input.");
        var input = JsonUtility.FromJson<Input>(File.ReadAllText(args[index + 1]));
        if (!string.IsNullOrEmpty(input.clientAssembly))
            ValidateSelectionBounds(input.clientAssembly);
        var bundles = new Dictionary<string, AssetBundle>();
        try
        {
            foreach (var name in input.bundles)
            {
                var bundle = AssetBundle.LoadFromFile(Path.Combine(input.directory, name + ".bundle"));
                if (!bundle || bundle.isStreamedSceneAssetBundle)
                    throw new InvalidOperationException("Not a loadable prop bundle: " + name);
                bundles.Add(name, bundle);
            }
            foreach (var entry in input.assets)
            {
                var prefab = bundles[entry.bundle].LoadAsset<GameObject>(entry.asset);
                if (!prefab)
                    throw new InvalidOperationException("Missing prop: " + entry.asset);
                var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length == 0)
                    throw new InvalidOperationException("Missing prop geometry: " + entry.asset);
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (!filter || !filter.sharedMesh || filter.sharedMesh.vertexCount == 0 || renderer.isPartOfStaticBatch)
                        throw new InvalidOperationException("Broken prop mesh: " + entry.asset);
                    if (renderer.sharedMaterials.Length == 0)
                        throw new InvalidOperationException("Missing prop materials: " + entry.asset);
                    foreach (var material in renderer.sharedMaterials)
                        if (!material || !material.shader)
                            throw new InvalidOperationException("Broken prop material/shader: " + entry.asset);
                }
                foreach (var group in prefab.GetComponentsInChildren<LODGroup>(true))
                foreach (var lod in group.GetLODs())
                foreach (var renderer in lod.renderers)
                    if (!renderer || !renderer.transform.IsChildOf(prefab.transform))
                        throw new InvalidOperationException("Broken prop LOD ownership: " + entry.asset);
                if (
                    Quaternion.Angle(prefab.transform.localRotation, entry.rotation) > 0.05f
                    || Vector3.Distance(prefab.transform.localScale, entry.scale) > 0.001f
                    || prefab.transform.localPosition.sqrMagnitude > 0.000001f
                )
                    throw new InvalidOperationException("Prop orientation/scale changed: " + entry.asset);
            }
            Debug.Log(
                "LEVEL PROP VALIDATION PASSED: " + input.assets.Length + " prefabs, meshes, materials, LOD ownership and source transforms."
            );
        }
        finally
        {
            foreach (var bundle in bundles.Values)
                bundle.Unload(true);
        }
    }

    private static void ValidateSelectionBounds(string clientPath)
    {
        var assembly = Assembly.Load(File.ReadAllBytes(clientPath));
        var type = assembly.GetType("WTT.Campaigns.Client.Authoring.Scenes.SceneBounds", true);
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var frameMethod = type.GetMethod("SelectionFrame", flags);
        var boundsMethod = type.GetMethod(
            "TryGet",
            flags,
            null,
            new[] { typeof(Transform), typeof(Bounds).MakeByRefType(), typeof(Bounds).MakeByRefType() },
            null
        );
        var wrapper = new GameObject("Generated prop placement root");
        var geometry = new GameObject("Native geometry frame");
        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var unrelated = new GameObject("Unrelated selection");
        try
        {
            geometry.transform.SetParent(wrapper.transform, false);
            mesh.transform.SetParent(geometry.transform, false);
            mesh.transform.localPosition = new Vector3(2, 1, -3);
            mesh.transform.localScale = new Vector3(12, 2.5f, 2.5f);
            geometry.transform.localRotation = Quaternion.Euler(0, 37, 0);
            geometry.transform.localScale = new Vector3(1.3f, .8f, 1.2f);
            foreach (var scale in new[] { Vector3.one, new Vector3(2, .75f, 1.2f), new Vector3(-2, .75f, 1.2f) })
            {
                wrapper.transform.SetPositionAndRotation(new Vector3(10, 4, -7), Quaternion.Euler(5, 30, 7));
                wrapper.transform.localScale = scale;
                var before = wrapper.transform.localToWorldMatrix;
                var frame = (Transform)frameMethod.Invoke(null, new object[] { wrapper.transform, geometry.transform });
                if (frame != geometry.transform)
                    throw new InvalidOperationException("Outline lost its native geometry axes.");
                var args = new object[] { frame, default(Bounds), default(Bounds) };
                if (!(bool)boundsMethod.Invoke(null, args))
                    throw new InvalidOperationException("No selection bounds.");
                var local = (Bounds)args[2];
                var source = mesh.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var signs = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    var actual = frame.TransformPoint(local.center + Vector3.Scale(local.extents, signs));
                    var expected = mesh.transform.TransformPoint(source.center + Vector3.Scale(source.extents, signs));
                    if (Vector3.Distance(actual, expected) > .001f)
                        throw new InvalidOperationException("Outline corner does not match posed mesh geometry.");
                }
                if (wrapper.transform.localToWorldMatrix != before)
                    throw new InvalidOperationException("Outline changed the saved placement pose.");
                var inflated = new object[] { wrapper.transform, default(Bounds), default(Bounds) };
                boundsMethod.Invoke(null, inflated);
                if (((Bounds)inflated[2]).size.z <= local.size.z * 1.5f)
                    throw new InvalidOperationException("Fixture failed to reproduce the old oversized wrapper outline.");
            }
            if (
                (Transform)frameMethod.Invoke(null, new object[] { wrapper.transform, unrelated.transform }) != wrapper.transform
                || (Transform)frameMethod.Invoke(null, new object[] { geometry.transform, null }) != geometry.transform
            )
                throw new InvalidOperationException("Ordinary selections or unrelated geometry changed selection ownership.");
            Debug.Log(
                "SELECTION BOUNDS PASSED: actual client code, native rotation, nested offset, nonuniform and mirrored scale, unchanged saved pose and ordinary-prop fallback."
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(wrapper);
            UnityEngine.Object.DestroyImmediate(unrelated);
        }
    }
}
