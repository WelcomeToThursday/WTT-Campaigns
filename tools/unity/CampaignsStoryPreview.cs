using System;
using System.IO;
using System.Linq;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using WTT.Campaigns.UI.Media;
using Object = UnityEngine.Object;

namespace WTT.Campaigns.Tools;

public static class CampaignsStoryPreview
{
    public static void Render()
    {
        RenderRoom(null);
    }

    public static void RenderPeacekeeper()
    {
        RenderRoom(CampaignsPeacekeeperBuilder.TraderId);
    }

    public static void RenderPrapor()
    {
        RenderRoom("54cb50c76803fa8b248b4571");
    }

    private static void RenderRoom(string selected)
    {
        var research = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/Story"));
        var audit = new JArray();
        foreach (var path in Directory.GetFiles(Path.Combine(research, "PreviewBundles"), "*.bundle"))
        {
            if (selected != null && Path.GetFileNameWithoutExtension(path) != selected)
            {
                continue;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bundle = AssetBundle.LoadFromFile(path) ?? throw new InvalidDataException("Cannot load preview bundle: " + path);
            var prefab = bundle.LoadAllAssets<GameObject>().Single();
            var room = StoryRoomCamera.InstantiateRoom(prefab);
            room.SetActive(false);
            var camera = StoryRoomCamera.Prepare(room, Path.GetFileNameWithoutExtension(path));
            camera.enabled = true;
            room.SetActive(true);
            room.GetComponent<StoryRoomIsolation>().Apply();
            room.GetComponent<StoryRoomLighting>().Apply();
            if (!camera.isActiveAndEnabled)
                throw new InvalidOperationException("Visit camera remains inactive: " + path);
            foreach (var animator in room.GetComponentsInChildren<Animator>(true))
            {
                if (!animator.runtimeAnimatorController)
                {
                    continue;
                }
                var idle =
                    animator.runtimeAnimatorController.animationClips.FirstOrDefault(c =>
                        c.name.EndsWith("_idle", StringComparison.OrdinalIgnoreCase)
                    ) ?? animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.Contains("Idle"));
                if (idle)
                {
                    idle.SampleAnimation(animator.gameObject, 1);
                }
            }
            var shaders = room.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m)
                .Select(m => m.shader)
                .Distinct()
                .ToArray();
            var unsupported = shaders.Where(s => !s || !s.isSupported).Select(s => s ? s.name : "<missing>").ToArray();
            var target = StoryRoomCamera.CreateTexture(1280, 720);
            camera.targetTexture = target;
            camera.GetComponent<StoryRoomRenderState>().BeginRender();
            camera.Render();
            camera.GetComponent<StoryRoomRenderState>().Restore();
            // Sample the room on the GPU as uGUI does. ReadPixels directly from
            // an RGB target does not populate an RGBA Texture2D's alpha channel.
            var sampled = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(target, sampled);
            RenderTexture.active = sampled;
            var image = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            var opaque = image.GetPixels32().Count(pixel => pixel.a == 255);
            if (opaque != image.width * image.height)
                throw new InvalidOperationException(
                    "Room render is transparent in uGUI: " + path + " format " + target.graphicsFormat + " opaque " + opaque
                );
            var output = Path.Combine(research, "Preview", Path.GetFileNameWithoutExtension(path) + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, image.EncodeToPNG());
            audit.Add(
                new JObject
                {
                    ["bundle"] = Path.GetFileName(path),
                    ["shaders"] = shaders.Length,
                    ["cameraActive"] = camera.isActiveAndEnabled,
                    ["opaquePixels"] = opaque,
                    ["textureFormat"] = target.format.ToString(),
                    ["runtimePosition"] = room.transform.position.ToString(),
                    ["unsupported"] = new JArray(unsupported),
                    ["image"] = output,
                }
            );
            RenderTexture.active = null;
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(sampled);
            room.SetActive(false);
            room.GetComponent<StoryRoomIsolation>().Restore();
            room.GetComponent<StoryRoomLighting>().Restore();
            Object.DestroyImmediate(room);
            bundle.Unload(true);
        }
        File.WriteAllText(Path.Combine(research, selected == null ? "trader-render.json" : selected + "-render.json"), audit.ToString());
        if (audit.Any(a => ((JArray)a["unsupported"]).Count > 0))
        {
            throw new InvalidDataException("Trader render contains unsupported shaders; see trader-render.json.");
        }
    }
}
