using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace SeasonalPerks.Tools;

public static class SeasonalStoryExampleBuilder
{
    public static void Verify()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        var bundle = AssetBundle.LoadFromFile(Path.Combine(project, "Client/Resources/StoryMedia/examples/story-test.bundle"));
        var prefab = bundle.LoadAsset<GameObject>("assets/story-test.prefab");
        if (!prefab || prefab.activeSelf)
        {
            throw new InvalidDataException("Example root must load inactive.");
        }
        var root = UnityEngine.Object.Instantiate(prefab);
        var director = root.GetComponent<PlayableDirector>();
        root.SetActive(true);
        director.time = 0;
        director.Evaluate();
        var start = root.transform.Find("Test marker").localPosition;
        director.time = 5;
        director.Evaluate();
        var end = root.transform.Find("Test marker").localPosition;
        if (Math.Abs(director.duration - 5) > .01 || Vector3.Distance(start, end) < 1.9f)
        {
            throw new InvalidDataException("The example timeline must bind and animate its marker for five seconds.");
        }
        var camera = root.GetComponentInChildren<Camera>();
        var target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(project, "Research/Story/Preview/cinematic-example.png"), image.EncodeToPNG());
        RenderTexture.active = null;
        director.Stop();
        root.SetActive(false);
        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        bundle.Unload(true);
        File.WriteAllText(
            Path.Combine(project, "Research/Story/example-checks.txt"),
            "Inactive prefab loads; native Timeline bindings resolve; marker moves two meters; duration is five seconds; rendered and unloaded. SDK evaluation only, not native beta completion-callback proof."
        );
    }

    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const string folder = "Assets/Mods/SeasonalPerks.Assets/StoryExamples";
        Directory.CreateDirectory(folder);
        var root = new GameObject("Synthetic story cinematic");
        root.SetActive(false);
        var camera = new GameObject("StoryCamera").AddComponent<Camera>();
        camera.transform.SetParent(root.transform, false);
        camera.transform.localPosition = new Vector3(0, 0, -5);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.07f, .12f, .15f);
        camera.cullingMask = 1 << 30;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Test marker";
        cube.transform.SetParent(root.transform, false);
        cube.layer = 30;
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        var material = new Material(Shader.Find("Unlit/Color"));
        material.color = new Color(.75f, .57f, .24f);
        AssetDatabase.CreateAsset(material, folder + "/marker.mat");
        cube.GetComponent<Renderer>().sharedMaterial = material;
        var animator = cube.AddComponent<Animator>();
        var clip = new AnimationClip { name = "Synthetic marker movement" };
        clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.EaseInOut(0, -1, 5, 1));
        AssetDatabase.CreateAsset(clip, folder + "/marker.anim");
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, folder + "/cinematic.playable");
        var track = timeline.CreateTrack<AnimationTrack>(null, "Marker");
        var timelineClip = track.CreateClip(clip);
        timelineClip.duration = 5;
        var director = root.AddComponent<PlayableDirector>();
        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.Hold;
        director.SetGenericBinding(track, animator);
        var prefab = folder + "/story-test.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefab);
        AssetDatabase.SaveAssets();
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/Story/ExampleBundles"));
        Directory.CreateDirectory(output);
        var manifest = BuildPipeline.BuildAssetBundles(
            output,
            new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = "examples/story-test.bundle",
                    assetNames = new[] { prefab },
                    addressableNames = new[] { "assets/story-test.prefab" },
                },
            },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64
        );
        if (!manifest)
        {
            throw new InvalidDataException("Story example bundle build failed.");
        }
        Debug.Log("Built the five-second synthetic cinematic example.");
    }
}
