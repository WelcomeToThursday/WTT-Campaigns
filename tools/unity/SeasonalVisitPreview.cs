using System;
using System.IO;
using System.Linq;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Media;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Screens;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeasonalPerks.Tools;

public static class SeasonalVisitPreview
{
    public static void Render()
    {
        foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1902, 992) })
            RenderAt(size);
    }

    private static void RenderAt(Vector2Int size)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/Story/Preview"));
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Mods/SeasonalPerks.Assets/Fonts/Bender.ttf");
        var camera = new GameObject("UI camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 5;
        var target = new RenderTexture(size.x, size.y, 24);
        camera.targetTexture = target;
        var canvas = new GameObject("Visit preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        var room = new Texture2D(2, 2);
        room.LoadImage(File.ReadAllBytes(Path.Combine(folder, "54cb50c76803fa8b248b4571.png")));
        var backdrop = UiElements.Rect("Room", canvas.transform, 0, 0);
        UiElements.Stretch(backdrop);
        backdrop.gameObject.AddComponent<RawImage>().texture = room;
        var selected = "";
        var close = 0;
        var skip = 0;
        var panel = new StoryConversationPanel(
            canvas.transform,
            font,
            id => selected = id,
            () => close++,
            () => skip++,
            StoryUiArtwork.Load("reply")
        );
        var replies = new[]
        {
            new StoryReplyView { Id = "trade", Text = "Want to trade?" },
            new StoryReplyView { Id = "tasks", Text = "Got any jobs for me?" },
        };
        void Check(bool pass, string text)
        {
            if (!pass)
                throw new InvalidOperationException(text);
        }
        void Layout()
        {
            foreach (var node in canvas.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = 5;
            Canvas.ForceUpdateCanvases();
        }
        Button Find(string name) => canvas.GetComponentsInChildren<Button>().Single(b => b.name == name);
        void Capture(string suffix)
        {
            Layout();
            foreach (var text in canvas.GetComponentsInChildren<Text>())
                font.RequestCharactersInTexture(text.text, text.fontSize);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(folder, "visit-" + size.y + "-" + suffix + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            RenderTexture.active = null;
        }
        panel.Set("Prapor", "", "", replies, false);
        Layout();
        Check(!canvas.GetComponentsInChildren<Button>().Any(b => b.name == "Skip playback"), "Idle visit exposes Skip");
        Check(((RectTransform)canvas.transform.Find("Story conversation")).rect.height < 160, "Empty dialogue wastes vertical space");
        Find("Want to trade?").onClick.Invoke();
        Check(selected == "trade", "Trade navigation callback failed");
        Capture("idle");
        panel.SetBusy(true, true);
        Check(!Find("Want to trade?").interactable, "Busy replies remain enabled");
        Find("Skip playback").onClick.Invoke();
        Check(skip == 1, "Playback skip callback failed");
        panel.Set(
            "Prapor",
            "A sample line used to check the dialogue layout.",
            "Earlier conversation.\n\nA sample line used to check the dialogue layout.",
            new[]
            {
                new StoryReplyView
                {
                    Id = "confirm",
                    Text = "Hand over the equipment.",
                    Confirmation = "Hand over the selected equipment?",
                },
            },
            false
        );
        Find("Hand over the equipment.").onClick.Invoke();
        Check(Find("Confirm").interactable, "Confirmation is inaccessible");
        Find("Cancel").onClick.Invoke();
        Find("Show history").onClick.Invoke();
        Check(Find("Hide history").interactable, "History toggle failed");
        Find("Hide history").onClick.Invoke();
        Find("Leave  [ESC]").onClick.Invoke();
        Check(close == 1, "Leave callback failed");
        panel.Set("Prapor", string.Join("\n", Enumerable.Repeat("Long history line for scrolling verification.", 60)), "", replies, false);
        Layout();
        var scroll = canvas.GetComponentInChildren<ScrollRect>();
        Check(scroll.content.rect.height > scroll.viewport.rect.height, "Long dialogue does not scroll");
        var panelRect = (RectTransform)canvas.transform.Find("Story conversation");
        Check(
            panelRect.rect.height + 150 <= ((RectTransform)canvas.transform).rect.height + 1,
            "Dialogue exceeds the available screen height"
        );
        panel.Set("Prapor", "A sample line used to check the dialogue layout.", "", replies, false);
        Capture("dialogue");
        panelRect.gameObject.SetActive(false);
        backdrop.gameObject.SetActive(false);
        var bar = UiElements.Rect("Trader header", canvas.transform, 0, 53);
        bar.anchorMin = new Vector2(0, .5f);
        bar.anchorMax = new Vector2(1, .5f);
        bar.sizeDelta = new Vector2(0, 53);
        var visitCount = 0;
        var visit = StoryVisitButton.Create(bar, font, () => visitCount++, _ => { });
        visit.onClick.Invoke();
        Check(visitCount == 1, "Visit callback failed");
        Capture("button-idle");
        visit.OnPointerEnter(new PointerEventData(null));
        Capture("button-hover");
        File.WriteAllText(
            Path.Combine(folder, "visit-checks-" + size.y + ".txt"),
            "Passed: compact idle, navigation, busy state, skip, confirmation, history, leave, long-text scrolling, screen bounds, Visit callback and hover.\n"
        );
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(room);
        Debug.Log("Visit UI checks passed at " + size);
    }
}
