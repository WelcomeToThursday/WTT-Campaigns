using System;
using System.IO;
using System.Linq;
using SeasonalPerks.UI.Media;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SeasonalPerks.Tools;

public static class SeasonalStoryMotionChecks
{
    public static void Run()
    {
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/Story"));
        var results = new JArray();
        foreach (var id in Directory.GetFiles(Path.Combine(folder, "PreviewBundles"), "*.bundle").Select(Path.GetFileNameWithoutExtension))
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bundle = AssetBundle.LoadFromFile(Path.Combine(folder, "PreviewBundles", id + ".bundle"));
            var room = StoryRoomCamera.InstantiateRoom(bundle.LoadAllAssets<GameObject>().Single());
            room.SetActive(false);
            var nativeCamera = new GameObject("Lobby camera").AddComponent<Camera>();
            var nativeLight = new GameObject("Lobby light").AddComponent<Light>();
            var nativeProbe = new GameObject("Lobby reflection").AddComponent<ReflectionProbe>();
            var cameraMask = nativeCamera.cullingMask;
            var lightMask = nativeLight.cullingMask;
            var camera = StoryRoomCamera.Prepare(room, id);
            room.SetActive(true);
            // The batch editor is not in Play mode; run the exact lifecycle methods.
            room.GetComponent<StoryRoomIsolation>().Apply();
            room.GetComponent<StoryRoomLighting>().Apply();
            if ((nativeCamera.cullingMask & (1 << 30)) != 0 || (nativeLight.cullingMask & (1 << 30)) != 0 || nativeProbe.enabled)
                throw new InvalidOperationException("Room isolation failed.");
            if (room.transform.position != Vector3.zero || camera.renderingPath != RenderingPath.DeferredShading)
                throw new InvalidOperationException("Room runtime configuration regressed.");
            var animator = room.GetComponentsInChildren<MonoBehaviour>(true)
                .Single(m => m.GetType().FullName == "EFT.AnimationSequencePlayer.SequenceReader")
                .GetComponent<Animator>();
            var clip = animator.runtimeAnimatorController.animationClips.First(c =>
                c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0
            );
            var skin = animator
                .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .OrderByDescending(s => s.sharedMesh.blendShapeCount)
                .First();
            var mesh = new Mesh();
            var nearFrames = new Vector3[120][];
            var farFrames = new Vector3[120][];
            double squaredError = 0;
            double maxError = 0;
            for (var frame = 0; frame < 120; frame++)
            {
                room.transform.position = Vector3.zero;
                clip.SampleAnimation(animator.gameObject, 1 + frame / 60f);
                skin.BakeMesh(mesh);
                nearFrames[frame] = mesh.vertices;
                room.transform.position = new Vector3(20000, 20000, 20000);
                // Identical local pose: this difference is coordinate precision, not animation.
                skin.BakeMesh(mesh);
                farFrames[frame] = mesh.vertices;
                for (var i = 0; i < mesh.vertexCount; i++)
                {
                    var error = Vector3.Distance(nearFrames[frame][i], farFrames[frame][i]);
                    squaredError += error * error;
                    maxError = Math.Max(maxError, error);
                }
            }
            double Acceleration(Vector3[][] frames)
            {
                double sum = 0;
                for (var f = 2; f < frames.Length; f++)
                for (var i = 0; i < frames[f].Length; i++)
                    sum += (frames[f][i] - 2 * frames[f - 1][i] + frames[f - 2][i]).sqrMagnitude;
                return Math.Sqrt(sum / ((frames.Length - 2) * frames[0].Length));
            }
            results.Add(
                new JObject
                {
                    ["trader"] = id,
                    ["clip"] = clip.name,
                    ["skin"] = skin.name,
                    ["rmsPositionErrorMillimeters"] = 1000 * Math.Sqrt(squaredError / (120 * mesh.vertexCount)),
                    ["maxPositionErrorMillimeters"] = 1000 * maxError,
                    ["nearFrameAcceleration"] = Acceleration(nearFrames),
                    ["farFrameAcceleration"] = Acceleration(farFrames),
                }
            );
            room.transform.position = Vector3.zero;
            clip.SampleAnimation(animator.gameObject, 1);
            var target = StoryRoomCamera.CreateTexture(1280, 720);
            camera.targetTexture = target;
            camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
            foreach (var path in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
            {
                camera.renderingPath = path;
                var ambientBefore = Shader.GetGlobalVector("_EFT_Ambient");
                var probeBefore = RenderSettings.ambientProbe;
                camera.GetComponent<StoryRoomRenderState>().BeginRender();
                camera.Render();
                camera.GetComponent<StoryRoomRenderState>().Restore();
                if (Shader.GetGlobalVector("_EFT_Ambient") != ambientBefore || RenderSettings.ambientProbe != probeBefore)
                    throw new InvalidOperationException("Room rendering leaked ambient settings to the lobby.");
                var sampled = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(target, sampled);
                RenderTexture.active = sampled;
                var image = new Texture2D(1280, 720, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(folder, "Jitter", id + "-" + path + ".png"), image.EncodeToPNG());
                RenderTexture.active = null;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(sampled);
            }
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(mesh);
            room.SetActive(false);
            room.GetComponent<StoryRoomIsolation>().Restore();
            room.GetComponent<StoryRoomLighting>().Restore();
            if (nativeCamera.cullingMask != cameraMask || nativeLight.cullingMask != lightMask || !nativeProbe.enabled)
                throw new InvalidOperationException("Room isolation did not restore the lobby.");
            Object.DestroyImmediate(room);
            bundle.Unload(true);
        }
        File.WriteAllText(Path.Combine(folder, "Jitter/motion-comparison.json"), results.ToString());
    }
}
