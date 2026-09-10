using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WTT.Campaigns.UI.Media;
using Object = UnityEngine.Object;

namespace WTT.Campaigns.Tools;

public static class CampaignsStoryBuilder
{
    private const string Root = "Assets/Mods/WTT-Campaigns.Assets/StoryTraders";

    public static void Build()
    {
        BuildRoom(null);
    }

    public static void BuildFence()
    {
        BuildRoom("579dc571d53a0658a154fbec");
    }

    public static void BuildPeacekeeper()
    {
        BuildRoom(CampaignsPeacekeeperBuilder.TraderId);
    }

    private static void BuildRoom(string selected)
    {
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        var imported = JObject.Parse(File.ReadAllText(Path.Combine(project, "Research/Story/trader-import.json")));
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        var builds = new List<AssetBundleBuild>();
        var audit =
            selected == null
                ? new JArray()
                : new JArray(
                    JArray
                        .Parse(File.ReadAllText(Path.Combine(project, "Research/Story/trader-build.json")))
                        .Where(a => (string)a["trader"] != selected)
                );
        foreach (var entry in ((JObject)imported["scenes"]).Properties().Where(p => selected == null || p.Name == selected))
        {
            var scene = EditorSceneManager.OpenScene((string)entry.Value, OpenSceneMode.Single);
            var root = new GameObject("Campaign trader " + entry.Name);
            root.SetActive(false);
            foreach (var original in scene.GetRootGameObjects().Where(g => g != root))
            {
                original.transform.SetParent(root.transform, true);
            }
            var missing = root.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if (missing > 0)
            {
                var nodes = root.GetComponentsInChildren<Transform>(true)
                    .Where(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    .Select(t =>
                        t.name + ": " + string.Join(", ", t.GetComponents<Component>().Select(c => c ? c.GetType().FullName : "<missing>"))
                    );
                throw new InvalidDataException(
                    "Trader " + entry.Name + " contains " + missing + " missing scripts: " + string.Join("; ", nodes)
                );
            }
            var readers = root.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(b => b.GetType().FullName == "EFT.AnimationSequencePlayer.SequenceReader")
                .ToArray();
            if (readers.Length != 1)
            {
                throw new InvalidDataException("Trader requires one sequence reader: " + entry.Name);
            }
            foreach (var type in new[] { "AnimationDictionary", "SecondaryAnimationDictionary", "LipSyncDictionary" })
            {
                var dictionary = readers[0].GetComponents<MonoBehaviour>().Single(b => b.GetType().Name == type);
                var entries = new SerializedObject(dictionary).FindProperty("entries");
                if (entries == null || entries.arraySize == 0)
                {
                    throw new InvalidDataException("Missing " + type + " entries for " + entry.Name);
                }
            }
            var cameras = root.GetComponentsInChildren<Camera>(true);
            var camera = cameras.SingleOrDefault(c => c.name.StartsWith("Camera_Debug", StringComparison.OrdinalIgnoreCase));
            if (!camera)
            {
                var anchor = root.GetComponentsInChildren<Transform>(true)
                    .SingleOrDefault(t => t.name.StartsWith("Position_Camera_", StringComparison.Ordinal));
                if (!anchor)
                {
                    throw new InvalidDataException("No reviewed room camera or authored camera position for " + entry.Name);
                }
                camera = new GameObject("StoryCamera").AddComponent<Camera>();
                camera.transform.SetParent(anchor, false);
                // Both supplied debug visit cameras use this field of view.
                camera.fieldOfView = 50;
                camera.nearClipPlane = .03f;
                camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.renderingPath = RenderingPath.DeferredShading;
                camera.allowHDR = true;
            }
            camera.name = "StoryCamera";
            camera.enabled = false;
            foreach (var other in cameras.Where(c => c != camera))
            {
                Object.DestroyImmediate(other.gameObject);
            }
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            var lighting = root.AddComponent<StoryRoomLighting>();
            lighting.Environment = StoryEnvironmentState.Capture();
            var lightmaps = LightmapSettings.lightmaps;
            lighting.Colors = lightmaps.Select(m => m.lightmapColor).ToArray();
            lighting.Directions = lightmaps.Select(m => m.lightmapDir).ToArray();
            lighting.Shadows = lightmaps.Select(m => m.shadowMask).ToArray();
            lighting.Renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.lightmapIndex >= 0 && r.lightmapIndex < lightmaps.Length)
                .ToArray();
            lighting.Indices = lighting.Renderers.Select(r => r.lightmapIndex).ToArray();
            lighting.Offsets = lighting.Renderers.Select(r => r.lightmapScaleOffset).ToArray();
            var path = Root + "/" + entry.Name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            audit.Add(
                new JObject
                {
                    ["trader"] = entry.Name,
                    ["prefab"] = path,
                    ["renderers"] = root.GetComponentsInChildren<Renderer>(true).Length,
                    ["animators"] = root.GetComponentsInChildren<Animator>(true).Length,
                    ["lightmaps"] = lightmaps.Length,
                    ["scripts"] = new JArray(
                        root.GetComponentsInChildren<MonoBehaviour>(true).Select(m => m.GetType().FullName).Distinct()
                    ),
                }
            );
            builds.Add(new AssetBundleBuild { assetBundleName = "traders/" + entry.Name + ".bundle", assetNames = new[] { path } });
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (selected == null || selected == CampaignsPeacekeeperBuilder.TraderId)
        {
            var peacekeeper = CampaignsPeacekeeperBuilder.Create();
            var peacekeeperPath = Root + "/" + CampaignsPeacekeeperBuilder.TraderId + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(peacekeeper, peacekeeperPath);
            audit.Add(
                new JObject
                {
                    ["trader"] = CampaignsPeacekeeperBuilder.TraderId,
                    ["prefab"] = peacekeeperPath,
                    ["custom"] = true,
                    ["renderers"] = peacekeeper.GetComponentsInChildren<Renderer>(true).Length,
                    ["animators"] = peacekeeper.GetComponentsInChildren<Animator>(true).Length,
                    ["lightmaps"] = 0,
                    ["speech"] = "Author-supplied audio; no recovered Peacekeeper recordings or facial blend shapes.",
                }
            );
            builds.Add(
                new AssetBundleBuild
                {
                    assetBundleName = "traders/" + CampaignsPeacekeeperBuilder.TraderId + ".bundle",
                    assetNames = new[] { peacekeeperPath },
                }
            );
        }
        AssetDatabase.SaveAssets();
        var output = Path.Combine(project, "Research/Story/Bundles");
        Directory.CreateDirectory(output);
        var result = BuildPipeline.BuildAssetBundles(
            output,
            builds.ToArray(),
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64
        );
        if (!result)
        {
            throw new InvalidDataException("Trader bundle build failed.");
        }
        File.WriteAllText(Path.Combine(project, "Research/Story/trader-build.json"), audit.ToString());
    }
}
