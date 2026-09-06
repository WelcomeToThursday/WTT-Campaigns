using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SeasonalItemBuilder
{
    private const string Root = "Assets/Mods/SeasonalPerks.Assets";
    private static readonly Dictionary<Object, Object> Copies = new Dictionary<Object, Object>();
    private static int _sequence;
    private static JObject _textures;

    public static void Build()
    {
        var manifest = JObject.Parse(File.ReadAllText(Root + "/Recovered/season-items.json"));
        var source = (string)manifest["liveWindows"];
        _textures = (JObject)manifest["textures"];
        var folder = Root + "/SeasonItems";
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        var bundles = new Dictionary<string, AssetBundle>();
        var builds = new List<AssetBundleBuild>();
        var keys = ((JArray)manifest["assets"]).SelectMany(a => a["dependencies"].Values<string>()).Distinct().ToArray();
        Copies.Clear();
        _sequence = 0;
        try
        {
            foreach (var key in keys)
            {
                bundles[key] =
                    AssetBundle.LoadFromFile(Path.Combine(source, key)) ?? throw new Exception("Missing item dependency: " + key);
            }
            foreach (JObject entry in manifest["assets"])
            {
                var key = (string)entry["key"];
                var bundle = AssetBundle.LoadFromFile(Path.Combine(source, key)) ?? throw new Exception("Cannot load item: " + key);
                bundles[key] = bundle;
                var originals = bundle.LoadAllAssets<GameObject>();
                if (originals.Length != 1)
                {
                    throw new Exception("Expected one item prefab: " + key);
                }
                var item = Object.Instantiate(originals[0]);
                item.name = originals[0].name;
                foreach (var transform in item.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    {
                        // Recovery audited the only source script as PreviewPivot. Replace its live MonoScript with the SDK type.
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
                    }
                    foreach (var behaviour in transform.GetComponents<MonoBehaviour>())
                    {
                        if (behaviour.GetType().Name != "PreviewPivot")
                        {
                            throw new Exception("Unreviewed item script: " + behaviour.GetType().FullName);
                        }
                    }
                }
                var pivotType = Type.GetType("PreviewPivot, Tarkov.Assembly", true);
                var pivot = item.GetComponent(pivotType) ?? item.AddComponent(pivotType);
                JsonUtility.FromJsonOverwrite(entry["pivot"].ToString(), pivot);
                foreach (var filter in item.GetComponentsInChildren<MeshFilter>(true))
                {
                    filter.sharedMesh = Copy(filter.sharedMesh, folder);
                }
                foreach (var collider in item.GetComponentsInChildren<MeshCollider>(true))
                {
                    collider.sharedMesh = Copy(collider.sharedMesh, folder);
                }
                foreach (var collider in item.GetComponentsInChildren<Collider>(true))
                {
                    collider.sharedMaterial = Copy(collider.sharedMaterial, folder);
                }
                foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(material => CopyMaterial(material, folder)).ToArray();
                }
                var path = folder + "/" + Path.GetFileNameWithoutExtension(key) + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(item, path);
                Object.DestroyImmediate(item);
                builds.Add(new AssetBundleBuild { assetBundleName = "wtt-seasonal/" + key, assetNames = new[] { path } });
            }
            AssetDatabase.SaveAssets();
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
            var output = Path.Combine(project, "Research/SeasonItems");
            Directory.CreateDirectory(output);
            var built = BuildPipeline.BuildAssetBundles(
                output,
                builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64
            );
            if (!built)
            {
                throw new Exception("Season item bundle build failed.");
            }
            File.WriteAllText(
                Path.Combine(output, "bundles.json"),
                new JObject
                {
                    ["manifest"] = new JArray(
                        builds.Select(b => new JObject
                        {
                            ["key"] = b.assetBundleName,
                            ["dependencyKeys"] = new JArray(built.GetAllDependencies(b.assetBundleName)),
                        })
                    ),
                }.ToString()
            );
            Debug.Log("Season items: rebuilt " + builds.Count + " prefabs using SDK script references.");
        }
        finally
        {
            foreach (var bundle in bundles.Values)
            {
                if (bundle)
                {
                    bundle.Unload(false);
                }
            }
            Copies.Clear();
        }
    }

    private static T Copy<T>(T original, string folder)
        where T : Object
    {
        if (!original)
        {
            return null;
        }
        if (Copies.TryGetValue(original, out var previous))
        {
            return (T)previous;
        }
        var value = Object.Instantiate(original);
        value.name = original.name;
        var path = folder + "/asset-" + (++_sequence).ToString("D4") + ".asset";
        if (File.Exists(path))
        {
            AssetDatabase.DeleteAsset(path);
        }
        AssetDatabase.CreateAsset(value, path);
        Copies.Add(original, value);
        return value;
    }

    private static Material CopyMaterial(Material original, string folder)
    {
        if (Copies.TryGetValue(original, out var previous))
        {
            return (Material)previous;
        }
        var material = new Material(original);
        material.shader = Copy(original.shader, folder);
        foreach (var property in original.GetTexturePropertyNames())
        {
            var texture = original.GetTexture(property);
            if (texture)
            {
                material.SetTexture(property, CopyTexture(texture, folder));
            }
        }
        var path = folder + "/material-" + (++_sequence).ToString("D4") + ".mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(material, path);
        Copies.Add(original, material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture CopyTexture(Texture original, string folder)
    {
        if (Copies.TryGetValue(original, out var previous))
        {
            return (Texture)previous;
        }
        var entry = _textures[original.name] ?? throw new Exception("Missing recovered texture: " + original.name);
        var bytes = File.ReadAllBytes(Root + "/Recovered/SeasonItemTextures/" + (string)entry["sha256"] + ".bytes");
        var format = (TextureFormat)(int)entry["format"];
        var mips = (int)entry["mips"];
        Texture texture;
        if ((string)entry["type"] == "Cubemap")
        {
            var cube = new Cubemap((int)entry["width"], format, mips);
            var offset = 0;
            for (var face = 0; face < 6; face++)
            {
                for (var mip = 0; mip < mips; mip++)
                {
                    cube.SetPixelData(bytes, mip, (CubemapFace)face, offset);
                    offset += cube.GetPixelData<byte>(mip, (CubemapFace)face).Length;
                }
            }
            if (offset != bytes.Length)
            {
                throw new Exception("Recovered cubemap byte count differs: " + original.name);
            }
            cube.Apply(false, false);
            texture = cube;
        }
        else
        {
            var flat = new Texture2D((int)entry["width"], (int)entry["height"], format, mips, (bool)entry["linear"]);
            flat.LoadRawTextureData(bytes);
            flat.Apply(false, false);
            texture = flat;
        }
        texture.name = original.name;
        texture.filterMode = original.filterMode;
        texture.anisoLevel = original.anisoLevel;
        texture.mipMapBias = original.mipMapBias;
        texture.wrapModeU = original.wrapModeU;
        texture.wrapModeV = original.wrapModeV;
        texture.wrapModeW = original.wrapModeW;
        var path = folder + "/texture-" + (string)entry["sha256"] + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(texture, path);
        Copies.Add(original, texture);
        return texture;
    }
}
