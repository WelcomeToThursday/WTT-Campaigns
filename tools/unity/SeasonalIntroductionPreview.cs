using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Screens;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SeasonalIntroductionPreview
{
    private const string Assets = "Assets/Mods/SeasonalPerks.Assets";

    public static void Render()
    {
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/UI"));
        var oldColorSpace = PlayerSettings.colorSpace;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        try
        {
            foreach (var resolution in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1902, 992) })
                RenderAt(output, resolution);
        }
        finally
        {
            PlayerSettings.colorSpace = oldColorSpace;
        }
    }

    private static void RenderAt(string output, Vector2Int resolution)
    {
        var checks = new List<string>();
        void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
            checks.Add(message);
        }
        var events = new GameObject("IntroductionEvents", typeof(EventSystem));
        // EventSystem is not ExecuteAlways; register it explicitly in the edit-mode runner.
        typeof(EventSystem)
            .GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(events.GetComponent<EventSystem>(), null);
        var cameraObject = new GameObject("IntroductionCamera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        camera.cullingMask = 1 << 5;
        var target = new RenderTexture(resolution.x, resolution.y, 24);
        camera.targetTexture = target;
        var canvasObject = new GameObject(
            "IntroductionCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1800, 980);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var view = new SeasonalScreen(
            canvasObject.transform,
            name => AssetDatabase.LoadAssetAtPath<GameObject>(Assets + "/UI/" + name + ".prefab"),
            AssetDatabase.LoadAssetAtPath<Font>(Assets + "/Fonts/Bender.ttf")
        );
        view.ArtworkRequested = (name, image) =>
        {
            var path = name.StartsWith("hub:") ? "HubArtwork/" + name.Substring(4) : "SelectionArtwork/" + name;
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Assets + "/" + path + ".png");
            Check(image.sprite, "Artwork available: " + name);
            image.enabled = true;
        };
        var switches = 0;
        var closes = 0;
        view.SwitchRequested = mode => switches++;
        view.CloseRequested = () => closes++;
        view.StartupSelection = true;
        var state = new ScreenState
        {
            Characters = new[]
            {
                new CharacterEntry { Mode = "normal", Exists = true },
                new CharacterEntry { Mode = "seasonal", Exists = false },
            },
        };
        view.SetState(state, ScreenPage.Characters);
        view.Open(ScreenPage.Characters);
        Button Find(string name) => view.Root.GetComponentsInChildren<Button>().Single(b => b.name == name);
        var info = Find("SeasonInformation");
        EventSystem.current.SetSelectedGameObject(info.gameObject);
        info.onClick.Invoke();
        Check(
            view.SeasonIntroductionOpen && view.DialogOpen && view.Page == ScreenPage.Characters,
            "Card info opens the introduction without changing the selector page"
        );
        Check(EventSystem.current.currentSelectedGameObject == null, "Underlying keyboard selection cleared");
        var body = view.Root.transform.Find("SafeArea/Page").GetComponent<CanvasGroup>();
        Check(!body.interactable && !body.blocksRaycasts, "Underlying selector input blocked");
        info.onClick.Invoke();
        Check(
            view.Root.GetComponentsInChildren<RectTransform>().Count(r => r.name == "SeasonsIntroduction") == 1,
            "Duplicate opening ignored"
        );
        view.ShowPage(ScreenPage.Global);
        Find("Select-normal").onClick.Invoke();
        Find("Select-seasonal").onClick.Invoke();
        Check(view.Page == ScreenPage.Characters && switches == 0, "Tutorial blocks page changes, selection and creation");
        for (var page = 0; page < 5; page++)
        {
            Check(view.SeasonIntroductionPage == page, "Page order " + page);
            foreach (var transform in canvasObject.GetComponentsInChildren<Transform>(true))
                transform.gameObject.layer = 5;
            Canvas.ForceUpdateCanvases();
            view.Fit();
            Canvas.ForceUpdateCanvases();
            var intro = view.Root.transform.Find("SafeArea/SeasonsIntroduction");
            var text = intro.GetComponentsInChildren<Text>().Single(t => t.name == "Body");
            Check(text.preferredHeight <= text.rectTransform.rect.height + 1, "Page text fits " + page);
            var corners = new Vector3[4];
            ((RectTransform)intro.Find("SeasonInfoCarousel")).GetWorldCorners(corners);
            Check(
                corners.All(p =>
                {
                    var v = camera.WorldToViewportPoint(p);
                    return v.x >= 0 && v.x <= 1 && v.y >= 0 && v.y <= 1;
                }),
                "Carousel inside viewport " + page
            );
            foreach (var label in intro.GetComponentsInChildren<Text>())
                label.font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var capture = new Texture2D(resolution.x, resolution.y, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
            capture.Apply();
            File.WriteAllBytes(
                Path.Combine(output, "season-introduction-" + (page + 1) + "-" + resolution.y + ".png"),
                capture.EncodeToPNG()
            );
            Object.DestroyImmediate(capture);
            RenderTexture.active = previous;
            Find("IntroductionNext").onClick.Invoke();
        }
        Check(view.SeasonIntroductionPage == 0, "Last page wraps to first");
        Find("IntroductionPrevious").onClick.Invoke();
        Check(view.SeasonIntroductionPage == 4, "First page wraps to last");
        view.RequestCloseFromInput();
        Check(!view.DialogOpen && !view.SeasonIntroductionOpen && closes == 0, "Escape closes only the introduction during startup");
        Check(
            body.interactable && body.blocksRaycasts && EventSystem.current.currentSelectedGameObject == info.gameObject,
            "Selector input and keyboard focus restored"
        );
        view.ShowSeasonIntroduction();
        Check(view.SeasonIntroductionPage == 0, "Replay starts at first page");
        view.SetBusy(true);
        Check(!view.SeasonIntroductionOpen && !view.DialogOpen, "Busy state cleans up introduction");
        view.ShowSeasonIntroduction();
        Check(!view.SeasonIntroductionOpen, "Busy state prevents opening");
        view.SetBusy(false);
        view.StartupSelection = false;
        state.Characters[1].Exists = true;
        view.SetState(state, ScreenPage.Characters);
        Find("SeasonInformation").onClick.Invoke();
        Check(view.SeasonIntroductionOpen, "Existing seasonal character can replay introduction");
        Find("IntroductionClose").onClick.Invoke();
        Check(!view.SeasonIntroductionOpen && closes == 0, "Close button returns to selector");
        view.ShowSeasonIntroduction();
        view.SetState(state, ScreenPage.Characters);
        Check(!view.SeasonIntroductionOpen && !view.DialogOpen, "Snapshot rebuild cleans up introduction");
        view.ShowSeasonIntroduction();
        view.Dispose();
        Check(!view.Root, "Dispose removes introduction and selector");
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(target);
        typeof(EventSystem)
            .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(events.GetComponent<EventSystem>(), null);
        Object.DestroyImmediate(events);
        File.WriteAllLines(Path.Combine(output, "season-introduction-checks-" + resolution.y + ".txt"), checks);
        Debug.Log("Season introduction: " + checks.Count + " checks passed at " + resolution);
    }
}
