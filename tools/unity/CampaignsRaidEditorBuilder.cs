using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Screens;

public static class CampaignsRaidEditorBuilder
{
    private const string Root = "Assets/Mods/WTT-Campaigns.Assets";

    private static GameObject Create()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/bender.ttf");
        var border = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/SelectionArtwork/confirmation-border.png");
        var header = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/SelectionArtwork/footer-gradient.png");
        if (!font || !border || !header)
            throw new InvalidOperationException("Recovered EFT font and window sprites are required.");
        return RaidEditorLayout.Build(font, border, header);
    }

    [MenuItem("SDK/WTT-Campaigns/Build raid editor")]
    public static void Build()
    {
        var root = Create();
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
        if (manifest.GetAllDependencies("wtt_campaigns_raid_editor.bundle").Length != 0)
            throw new Exception("Raid editor bundle must be self-contained.");
        Preview();
        var bundle = AssetBundle.LoadFromFile(Path.Combine(output, "wtt_campaigns_raid_editor.bundle"));
        if (!bundle)
            throw new Exception("Cannot reload editor bundle.");
        var loaded = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(prefab.ToLowerInvariant()));
        Validate(loaded);
        UnityEngine.Object.DestroyImmediate(loaded);
        bundle.Unload(true);
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var hash = BitConverter
                .ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(output, "wtt_campaigns_raid_editor.bundle"))))
                .Replace("-", "");
            File.WriteAllText(
                Path.Combine(output, "raid-editor-validation.json"),
                "{\"schema\":2,\"sha256\":\"" + hash + "\",\"validated\":true}"
            );
        }
    }

    private static void Validate(GameObject root)
    {
        void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            Check(component && component.GetType().Namespace == "UnityEngine.UI", "Bundle contains a missing or non-native UI script.");
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.ForceUpdateCanvases();
        var host = root.AddComponent<RaidEditorWindows>();
        host.Initialize();
        foreach (var module in RaidEditorLayout.Modules)
        {
            var panel = root.transform.Find("Workspace/DockArea/" + module.Id);
            var field = panel.GetComponentInChildren<InputField>();
            var value = field.text;
            field.text = "Unsaved draft 42";
            var toggle = panel.Find(module.Id + "TitleBar/" + module.Id + "Popout").GetComponent<Button>();
            toggle.onClick.Invoke();
            Check(panel.parent == root.transform, "Panel did not detach: " + module.Id);
            Check(field.text == "Unsaved draft 42", "Detach lost draft input.");
            var drag = panel.GetComponentInChildren<EditorWindowDrag>();
            ((RectTransform)panel).anchoredPosition = new Vector2(99999, -99999);
            drag.Clamp();
            var corners = new Vector3[4];
            ((RectTransform)panel).GetWorldCorners(corners);
            foreach (var corner in corners)
                Check(
                    ((RectTransform)root.transform).rect.Contains(root.transform.InverseTransformPoint(corner) * .999f),
                    "Popout left canvas bounds."
                );
            toggle.onClick.Invoke();
            Check(panel.parent.name == "DockArea", "Panel did not dock.");
            Check(field.text == "Unsaved draft 42", "Dock lost draft input.");
            toggle.onClick.Invoke();
            host.ResetLayout();
            Check(panel.parent.name == "DockArea", "Reset did not restore panel.");
            field.text = value;
        }
        var shield = root.transform.Find("ConflictShield");
        shield.gameObject.SetActive(true);
        host.KeepModalOnTop();
        Check(
            shield.GetSiblingIndex() == root.transform.childCount - 1 && shield.GetComponent<Image>().raycastTarget,
            "Conflict must block every window."
        );
        shield.gameObject.SetActive(false);
        UnityEngine.Object.DestroyImmediate(host);
        foreach (var drag in root.GetComponentsInChildren<EditorWindowDrag>(true))
            UnityEngine.Object.DestroyImmediate(drag);
        Debug.Log("Raid editor: detach, dock, draft retention, reset, clamping and modal checks passed.");
    }

    [MenuItem("SDK/WTT-Campaigns/Preview raid editor")]
    public static void Preview()
    {
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/UI/RaidEditor"));
        Directory.CreateDirectory(output);
        foreach (
            var size in new[]
            {
                new Vector2Int(1920, 1080),
                new Vector2Int(2560, 1440),
                new Vector2Int(3440, 1440),
                new Vector2Int(1280, 1024),
            }
        )
        {
            var root = Create();
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
            var host = root.AddComponent<RaidEditorWindows>();
            host.Initialize();
            foreach (var state in new[] { "docked", "popout", "conflict" })
            {
                if (state == "popout")
                    root.transform.Find("Workspace/DockArea/Inspector/InspectorTitleBar/InspectorPopout")
                        .GetComponent<Button>()
                        .onClick.Invoke();
                if (state == "conflict")
                {
                    root.transform.Find("ConflictShield").gameObject.SetActive(true);
                    host.KeepModalOnTop();
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(output, size.x + "x" + size.y + "-" + state + ".png"), texture.EncodeToPNG());
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture);
            }
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(target);
        }
        Debug.Log("Campaign raid editor previews: " + output);
    }
}
