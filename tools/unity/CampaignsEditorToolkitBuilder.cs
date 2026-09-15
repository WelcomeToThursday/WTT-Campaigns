using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Asset-only SDK build; never launches a game or server.
public static class CampaignsEditorToolkitBuilder
{
    public static void Build()
    {
        const string folder = "Assets/Mods/WTT-Campaigns.Assets/EditorToolkit";
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        Directory.CreateDirectory(folder);
        foreach (var file in new[] { "Editor.uss", "Editor.uxml" })
            File.Copy(Path.Combine(project, "tools/unity/EditorToolkit", file), folder + "/" + file, true);
        File.WriteAllText(folder + "/Editor.tss", "@import url(\"unity-theme://default\");\n");
        AssetDatabase.Refresh();
        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(folder + "/Editor.tss");
        var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(folder + "/Editor.uxml");
        if (!theme || !tree || tree.CloneTree().Q("surface") == null)
            throw new InvalidOperationException("Editor Toolkit theme or visual tree failed to import.");
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = theme;
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        var path = folder + "/EditorPanel.asset";
        if (AssetDatabase.LoadAssetAtPath<PanelSettings>(path))
            AssetDatabase.DeleteAsset(path);
        // Built-in shader references can point to assets stripped from EFT. Clone the
        // compiled engine shaders into owned assets and prove they survive the bundle.
        var serialized = new SerializedObject(settings);
        foreach (var property in new[] { "m_AtlasBlitShader", "m_RuntimeShader", "m_RuntimeWorldShader" })
        {
            var field = serialized.FindProperty(property);
            if (field == null || !field.objectReferenceValue)
                throw new Exception("Missing panel shader: " + property);
            var shader = UnityEngine.Object.Instantiate(field.objectReferenceValue);
            var shaderPath = folder + "/" + property + ".asset";
            if (AssetDatabase.LoadMainAssetAtPath(shaderPath))
                AssetDatabase.DeleteAsset(shaderPath);
            AssetDatabase.CreateAsset(shader, shaderPath);
            field.objectReferenceValue = shader;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(settings, path);
        // Serialized PanelSettings includes the engine's UI shaders so the mod does not
        // depend on EFT having kept unused runtime Toolkit shaders in its player build.
        AssetDatabase.SaveAssets();
        var output = Path.Combine(project, "Client/Resources");
        const string bundleName = "wtt_campaigns_editor_toolkit.bundle";
        var manifest = BuildPipeline.BuildAssetBundles(
            output,
            new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = bundleName,
                    assetNames = new[]
                    {
                        path,
                        folder + "/Editor.uxml",
                        folder + "/Editor.uss",
                        folder + "/m_RuntimeShader.asset",
                        folder + "/m_RuntimeWorldShader.asset",
                        folder + "/m_AtlasBlitShader.asset",
                        "Assets/Mods/WTT-Campaigns.Assets/Fonts/bender.ttf",
                        "Assets/Mods/WTT-Campaigns.Assets/RaidEditor/CampaignScenePreview.shader",
                    },
                },
            },
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64
        );
        if (!manifest || manifest.GetAllDependencies(bundleName).Length != 0)
            throw new InvalidOperationException("Editor Toolkit bundle must be self-contained.");
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, bundleName));
        if (!bundle || !bundle.LoadAsset<PanelSettings>(path) || !bundle.LoadAsset<VisualTreeAsset>(folder + "/Editor.uxml"))
            throw new InvalidOperationException("Editor Toolkit bundle reload failed.");
        if (bundle.LoadAllAssets<Shader>().Length < 3)
            throw new InvalidOperationException("Runtime UI Toolkit shaders were not embedded in the bundle.");
        bundle.Unload(true);
        using (var sha = System.Security.Cryptography.SHA256.Create())
            File.WriteAllText(
                Path.Combine(output, "editor-toolkit-validation.json"),
                "{\"schema\":1,\"unity\":\""
                    + Application.unityVersion
                    + "\",\"validated\":true,\"sha256\":\""
                    + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(output, bundleName)))).Replace("-", "")
                    + "\"}"
            );
        Debug.Log("Editor Toolkit assets imported, bundled and reloaded successfully.");
    }
}
