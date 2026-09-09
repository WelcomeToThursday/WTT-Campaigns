using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SeasonalPerks.UI.Controls;
using TMPro;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeasonalPerks.Tools
{
    public static class SeasonalStoryNotificationBuilder
    {
        private const string Root = "Assets/Mods/SeasonalPerks.Assets";
        private const string Folder = Root + "/StoryNotifications";
        private static readonly string[] States = { "Started", "Complete", "Failed" };

        public static void Build()
        {
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
            var output = Path.Combine(project, "Client/Resources");
            var existing = AssetBundle.LoadFromFile(Path.Combine(output, "seasonalperks_ui.bundle"));
            if (!existing)
                throw new InvalidDataException("Build the existing main UI bundle before adding chapter notifications.");
            var previousNames = existing.GetAllAssetNames();
            existing.Unload(true);
            var sourcePaths = AssetDatabase.GetAllAssetPaths().ToDictionary(p => p, StringComparer.OrdinalIgnoreCase);
            var mainAssets = previousNames
                .Select(p =>
                    sourcePaths.TryGetValue(p, out var source) ? source : throw new InvalidDataException("Missing existing UI source: " + p)
                )
                .ToList();
            AssetDatabase.Refresh();
            foreach (
                var path in Directory.GetFiles(Folder + "/Artwork", "*.png").Concat(Directory.GetFiles(Root + "/StoryStatusIcons", "*.png"))
            )
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            foreach (var path in Directory.GetFiles(Folder + "/Audio", "*.wav"))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            var fontPath = Folder + "/Bender SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (!font)
            {
                font = TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Bender.ttf"));
                font.name = "Bender Story Notifications";
                AssetDatabase.CreateAsset(font, fontPath);
                AssetDatabase.AddObjectToAsset(font.material, font);
                foreach (var atlas in font.atlasTextures)
                    AssetDatabase.AddObjectToAsset(atlas, font);
            }
            font.TryAddCharacters(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /:.-!?Chapter startedcompletefailedIn Raid Test"
            );
            EditorUtility.SetDirty(font);
            var controller = Controller();
            var prefabs = new List<string>();
            foreach (var state in States)
            {
                var banner = new StoryChapterBanner(null, font, "Chapter title", Caption(state), state);
                var animator = banner.Root.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                var sound = banner.Root.gameObject.AddComponent<AudioSource>();
                sound.playOnAwake = false;
                sound.spatialBlend = 0;
                sound.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/Audio/" + state.ToLowerInvariant() + ".wav");
                var prefab = Folder + "/SeasonalChapter" + state + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(banner.Root.gameObject, prefab);
                Object.DestroyImmediate(banner.Root.gameObject);
                prefabs.Add(prefab);
            }
            mainAssets.AddRange(Directory.GetFiles(Root + "/StoryStatusIcons", "*.png"));
            mainAssets.Add(fontPath);
            AssetDatabase.SaveAssets();
            var backup = Path.Combine(project, "artifacts/notification-bundle-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            File.Copy(Path.Combine(output, "seasonalperks_ui.bundle"), Path.Combine(backup, "seasonalperks_ui.bundle"));
            var manifest = BuildPipeline.BuildAssetBundles(
                output,
                new[]
                {
                    new AssetBundleBuild
                    {
                        assetBundleName = "seasonalperks_ui.bundle",
                        assetNames = mainAssets.Select(p => p.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    },
                    new AssetBundleBuild { assetBundleName = "seasonal_story_notifications.bundle", assetNames = prefabs.ToArray() },
                },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64
            );
            if (!manifest || !manifest.GetAllDependencies("seasonal_story_notifications.bundle").Contains("seasonalperks_ui.bundle"))
                throw new InvalidDataException("Chapter prefab bundle must depend on the main UI's shared status icons.");
            var dependencies = manifest.GetAllDependencies("seasonal_story_notifications.bundle");
            var main = AssetBundle.LoadFromFile(Path.Combine(output, "seasonalperks_ui.bundle"));
            var notifications = AssetBundle.LoadFromFile(Path.Combine(output, "seasonal_story_notifications.bundle"));
            try
            {
                if (previousNames.Except(main.GetAllAssetNames()).Any())
                    throw new InvalidDataException("Existing UI assets were lost.");
                Preview(notifications, project);
            }
            finally
            {
                notifications.Unload(true);
                main.Unload(true);
            }
            var checks = new JObject
            {
                ["prefabs"] = new JArray(prefabs),
                ["dependencies"] = new JArray(dependencies),
                ["bundles"] = new JArray(
                    new[] { "seasonalperks_ui.bundle", "seasonal_story_notifications.bundle" }.Select(name => new JObject
                    {
                        ["file"] = name,
                        ["sha256"] = Hash(Path.Combine(output, name)),
                    })
                ),
            };
            File.WriteAllText(Path.Combine(output, "story-notification-validation.json"), checks.ToString());
            Debug.Log("Story notification bundles built, reloaded, layout checked and rendered at 1080p, 1440p and ultrawide.");
        }

        private static AnimatorController Controller()
        {
            var show = new AnimationClip { name = "ChapterShow", frameRate = 60 };
            show.SetCurve(
                "",
                typeof(CanvasGroup),
                "m_Alpha",
                new AnimationCurve(new Keyframe(0, 0), new Keyframe(1f / 3, 1), new Keyframe(8f / 3, 1))
            );
            var hide = new AnimationClip { name = "ChapterHide", frameRate = 60 };
            hide.SetCurve("", typeof(CanvasGroup), "m_Alpha", AnimationCurve.EaseInOut(0, 1, .25f, 0));
            AnimationUtility.SetAnimationEvents(
                hide,
                new[]
                {
                    new AnimationEvent { time = .25f, functionName = "OnAnimationDone" },
                }
            );
            show = Save(show, Folder + "/ChapterShow.anim");
            hide = Save(hide, Folder + "/ChapterHide.anim");
            var path = Folder + "/Chapter.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path))
                AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter(
                new AnimatorControllerParameter
                {
                    name = "Speed",
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 1,
                }
            );
            controller.AddParameter(
                new AnimatorControllerParameter
                {
                    name = "AutoComplete",
                    type = AnimatorControllerParameterType.Bool,
                    defaultBool = true,
                }
            );
            controller.AddParameter("ForcedComplete", AnimatorControllerParameterType.Trigger);
            var machine = controller.layers[0].stateMachine;
            var showing = machine.AddState("ShowNotification");
            showing.motion = show;
            var hiding = machine.AddState("HideNotification");
            hiding.motion = hide;
            foreach (var state in new[] { showing, hiding })
            {
                state.speedParameter = "Speed";
                state.speedParameterActive = true;
            }
            machine.defaultState = showing;
            var automatic = showing.AddTransition(hiding);
            automatic.hasExitTime = true;
            automatic.exitTime = 1;
            automatic.duration = 0;
            automatic.AddCondition(AnimatorConditionMode.If, 0, "AutoComplete");
            var forced = showing.AddTransition(hiding);
            forced.hasExitTime = false;
            forced.duration = 0;
            forced.AddCondition(AnimatorConditionMode.If, 0, "ForcedComplete");
            return controller;
        }

        private static AnimationClip Save(AnimationClip clip, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                Object.DestroyImmediate(clip);
                return existing;
            }
            else
                AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static string Caption(string state) =>
            state == "Complete" ? "Chapter complete"
            : state == "Failed" ? "Chapter failed"
            : "Chapter started";

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        private static void Preview(AssetBundle bundle, string project)
        {
            var output = Path.Combine(project, "Research/Story/NotificationPreview");
            Directory.CreateDirectory(output);
            var reference = JObject.Parse(File.ReadAllText(Path.Combine(project, "Research/Story/level49-2206.json")));
            var artworkPath = Environment.GetEnvironmentVariable("SEASONAL_NOTIFICATION_PREVIEW_ARTWORK");
            var reports = new JArray();
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3440, 1440) })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Texture2D artworkTexture = null;
                Sprite artwork = null;
                if (!string.IsNullOrEmpty(artworkPath))
                {
                    artworkTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!artworkTexture.LoadImage(File.ReadAllBytes(artworkPath)))
                        throw new InvalidDataException("Invalid notification preview artwork.");
                    artwork = Sprite.Create(
                        artworkTexture,
                        new Rect(0, 0, artworkTexture.width, artworkTexture.height),
                        new Vector2(.5f, .5f)
                    );
                }
                var camera = new GameObject("Preview camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.14f, .15f, .13f);
                var target = new RenderTexture(size.x, size.y, 24);
                camera.targetTexture = target;
                var canvas = new GameObject("Preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 1;
                var container = UiElements.Rect("Notifications", canvas.transform, 500, 400);
                container.anchorMin = container.anchorMax = new Vector2(1, 1);
                container.pivot = new Vector2(1, 1);
                container.anchoredPosition = new Vector2(-20, -80);
                var group = container.gameObject.AddComponent<VerticalLayoutGroup>();
                group.childControlHeight = true;
                group.childControlWidth = false;
                group.childForceExpandHeight = group.childForceExpandWidth = false;
                group.spacing = 12;
                foreach (var state in States)
                {
                    var prefab = bundle.LoadAsset<GameObject>((Folder + "/SeasonalChapter" + state + ".prefab").ToLowerInvariant());
                    if (
                        !prefab
                        || prefab
                            .GetComponentsInChildren<Transform>(true)
                            .Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)
                    )
                        throw new InvalidDataException("Notification prefab has missing components.");
                    var view = Object.Instantiate(prefab, container, false);
                    ValidateReferenceArtwork(view.transform, reference);
                    if (artwork)
                        view.transform.Find("Content/Left/Icon").GetComponent<Image>().sprite = artwork;
                    view.GetComponent<Animator>().enabled = false;
                    view.GetComponent<AudioSource>().enabled = false;
                    view.transform.Find("Content/Text group/Title").GetComponent<TMP_Text>().text =
                        state == "Started"
                            ? "In Raid Test"
                            : "A long chapter title must stay inside its banner without pushing the status icon away";
                    view.SetActive(true);
                    var statusText = view.transform.Find("Content/Text group/Text/Text").GetComponent<TMP_Text>();
                    statusText.ForceMeshUpdate();
                    statusText.GetComponent<LayoutElement>().preferredWidth = Mathf.Min(395, statusText.GetPreferredValues().x);
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(container);
                    var content = (RectTransform)view.transform.Find("Content");
                    var checkmark = (RectTransform)view.transform.Find("Content/Text group/Text/Icon");
                    var corners = new Vector3[4];
                    checkmark.GetWorldCorners(corners);
                    if (
                        Mathf.Abs(content.rect.width - 500) > .1f
                        || Mathf.Abs(content.rect.height - 48.22f) > .1f
                        || statusText.rectTransform.rect.width < 80
                        || corners.Any(p => !content.rect.Contains(content.InverseTransformPoint(p)))
                    )
                        throw new InvalidDataException("Notification layout clips its status indicator.");
                    if (!view.GetComponent<AudioSource>().clip || !view.transform.Find("Content/bg").GetComponent<Image>().sprite)
                        throw new InvalidDataException("Notification bundle lost its static artwork or sound.");
                    if (!view.transform.Find("Content/bg").GetComponent<Image>().raycastTarget)
                        throw new InvalidDataException("Notification banner must receive native click-to-dismiss input.");
                    reports.Add(
                        new JObject
                        {
                            ["size"] = size.ToString(),
                            ["status"] = state,
                            ["width"] = content.rect.width,
                            ["height"] = content.rect.height,
                            ["referenceArtworkLayout"] = true,
                            ["authoredArtwork"] = artwork != null,
                        }
                    );
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, size.x + "x" + size.y + ".png"), image.EncodeToPNG());
                var scale = size.y / 1080f;
                var detail = new Texture2D(Mathf.RoundToInt(520 * scale), Mathf.RoundToInt(194 * scale), TextureFormat.RGB24, false);
                detail.ReadPixels(
                    new Rect(size.x - detail.width, size.y - Mathf.RoundToInt(70 * scale) - detail.height, detail.width, detail.height),
                    0,
                    0
                );
                detail.Apply();
                File.WriteAllBytes(Path.Combine(output, size.x + "x" + size.y + "-detail.png"), detail.EncodeToPNG());
                Object.DestroyImmediate(detail);
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(canvas.gameObject);
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
                if (artwork)
                    Object.DestroyImmediate(artwork);
                if (artworkTexture)
                    Object.DestroyImmediate(artworkTexture);
            }
            File.WriteAllText(Path.Combine(output, "checks.json"), reports.ToString());
        }

        private static void ValidateReferenceArtwork(Transform root, JObject reference)
        {
            foreach (var path in new[] { "Content", "Content/bg", "Content/Left", "Content/Left/Icon" })
            {
                var node = reference;
                foreach (var name in path.Split('/'))
                    node = (JObject)node["children"].Single(child => (string)child["name"] == name);
                var fields = node["components"].Single(c => (string)c["type"] == "UnityEngine.UI.Image")["fields"];
                var actual = root.Find(path).GetComponent<Image>();
                if (actual.enabled != ((int)fields["m_Enabled"] != 0))
                    throw new InvalidDataException("Notification image visibility differs from live: " + path);
                if (path == "Content/Left/Icon")
                {
                    var size = node["rect"]["m_SizeDelta"];
                    var position = node["rect"]["m_AnchoredPosition"];
                    if (
                        Vector2.Distance(actual.rectTransform.sizeDelta, new Vector2((float)size["x"], (float)size["y"])) > .01f
                        || Vector2.Distance(actual.rectTransform.anchoredPosition, new Vector2((float)position["x"], (float)position["y"]))
                            > .01f
                        || !actual.preserveAspect
                        || actual.rectTransform.localScale != Vector3.one
                    )
                        throw new InvalidDataException("Notification chapter icon geometry differs from live.");
                }
            }
        }
    }
}
