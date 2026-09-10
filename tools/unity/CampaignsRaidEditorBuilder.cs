using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Screens;

public static class CampaignsRaidEditorBuilder
{
    private const string Root = "Assets/Mods/WTT-Campaigns.Assets";

    [MenuItem("SDK/WTT-Campaigns/Build raid editor")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/bender.ttf");
        if (!font)
            throw new InvalidOperationException("The recovered Tarkov Bender font is required.");
        var root = RaidEditorLayout.Build(font);
        Directory.CreateDirectory(Root + "/RaidEditor");
        var prefab = Root + "/RaidEditor/SeasonalRaidEditor.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefab);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        var output = Path.Combine(project, "Client/Resources");
        var manifest = BuildPipeline.BuildAssetBundles(
            output,
            new[]
            {
                new AssetBundleBuild { assetBundleName = "wtt_campaigns_raid_editor.bundle", assetNames = new[] { prefab } },
            },
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64
        );
        if (!manifest)
            throw new InvalidOperationException("Raid editor bundle build failed.");
        Preview();
    }

    [MenuItem("SDK/WTT-Campaigns/Preview raid editor")]
    public static void Preview()
    {
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/UI/RaidEditor"));
        Directory.CreateDirectory(output);
        var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/bender.ttf");
        foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3440, 1440) })
        {
            var root = RaidEditorLayout.Build(font);
            var cameraObject = new GameObject("Raid editor preview camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.17f, .18f, .15f);
            var target = new RenderTexture(size.x, size.y, 24);
            camera.targetTexture = target;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(output, size.x + "x" + size.y + ".png"), texture.EncodeToPNG());
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(target);
        }
        Debug.Log("Campaign raid editor previews: " + output);
    }
}
