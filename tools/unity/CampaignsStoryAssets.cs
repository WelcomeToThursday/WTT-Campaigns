using System;
using System.IO;
using System.Linq;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WTT.Campaigns.Tools;

public static class CampaignsStoryAssets
{
    public static void Inventory()
    {
        // Preserve compiled beta shaders; AssetRipper's shader stubs have no rendering implementation.
        var shaderFolder = "Assets/Mods/WTT-Campaigns.Assets/StoryNativeShaders";
        Directory.CreateDirectory(shaderFolder);
        var betaShaders = AssetBundle.LoadFromFile(
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../EscapeFromTarkov_Data/StreamingAssets/Windows/shaders"))
        );
        if (!betaShaders)
        {
            throw new InvalidDataException("Cannot load the beta shader bundle.");
        }
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var original in betaShaders.LoadAllAssets<Shader>())
            {
                var hash = System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(original.name));
                var path = shaderFolder + "/" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant() + ".asset";
                if (!File.Exists(path))
                {
                    var copy = UnityEngine.Object.Instantiate(original);
                    copy.name = original.name;
                    AssetDatabase.CreateAsset(copy, path);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        betaShaders.Unload(false);
        AssetDatabase.SaveAssets();
        var scripts = new JObject();
        foreach (var path in AssetDatabase.GetAllAssetPaths().Where(p => p.EndsWith(".dll") || p.EndsWith(".cs")))
        {
            foreach (var script in AssetDatabase.LoadAllAssetsAtPath(path).OfType<MonoScript>())
            {
                var type = script.GetClass();
                if (type == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid, out long id))
                {
                    continue;
                }
                scripts[type.FullName] = new JObject
                {
                    ["guid"] = guid,
                    ["fileID"] = id,
                    ["assembly"] = type.Assembly.GetName().Name,
                    ["path"] = path,
                };
            }
        }
        var shaders = new JObject();
        foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { shaderFolder }))
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
            if (shader && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string assetGuid, out long id))
            {
                shaders[shader.name] = new JObject
                {
                    ["guid"] = assetGuid,
                    ["fileID"] = id,
                    ["path"] = AssetDatabase.GetAssetPath(shader),
                };
            }
        }
        foreach (var shaderInfo in ShaderUtil.GetAllShaderInfo())
        {
            var shader = Shader.Find(shaderInfo.name);
            if (
                shader
                && shaders[shader.name] == null
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string guid, out long id)
            )
            {
                shaders[shader.name] = new JObject
                {
                    ["guid"] = guid,
                    ["fileID"] = id,
                    ["path"] = AssetDatabase.GetAssetPath(shader),
                };
            }
        }
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        File.WriteAllText(
            Path.Combine(project, "Research/Story/sdk-assets.json"),
            new JObject { ["scripts"] = scripts, ["shaders"] = shaders }.ToString()
        );
        Debug.Log("Story SDK inventory: " + scripts.Count + " scripts, " + shaders.Count + " shaders.");
    }
}
