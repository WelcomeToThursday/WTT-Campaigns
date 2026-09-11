using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Media;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using Object = UnityEngine.Object;

namespace WTT.Campaigns.Tools;

public static class CampaignsVisitPreview
{
    public static void Render()
    {
        var custom = new GameObject("Custom room validation");
        var camera = new GameObject("StoryCamera");
        camera.transform.SetParent(custom.transform);
        camera.AddComponent<Camera>();
        bool Rejected()
        {
            try
            {
                Object.DestroyImmediate(StoryRoomCamera.InstantiateCustomRoom(custom));
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
        if (!Rejected())
            throw new InvalidOperationException("Active custom rooms must be rejected before instantiation.");
        custom.SetActive(false);
        var instance = StoryRoomCamera.InstantiateCustomRoom(custom);
        if (instance.activeSelf)
            throw new InvalidOperationException("Custom room activated before preparation.");
        StoryRoomCamera.Prepare(instance);
        Object.DestroyImmediate(instance);
        camera.name = "OtherCamera";
        if (!Rejected())
            throw new InvalidOperationException("Missing StoryCamera must be rejected.");
        camera.name = "StoryCamera";
        var duplicate = new GameObject("StoryCamera");
        duplicate.transform.SetParent(custom.transform);
        duplicate.AddComponent<Camera>();
        if (!Rejected())
            throw new InvalidOperationException("Ambiguous custom cameras must be rejected.");
        Object.DestroyImmediate(custom);
        foreach (
            var size in new[]
            {
                new Vector2Int(1280, 720),
                new Vector2Int(1024, 768),
                new Vector2Int(1920, 1080),
                new Vector2Int(2560, 1440),
                new Vector2Int(3440, 1440),
            }
        )
            RenderAt(size);
    }

    private static void RenderAt(Vector2Int size)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/Story/Preview"));
        var compactRoom = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../../SeasonalPerks/artifacts/compact-preview/Preview/54cb50c76803fa8b248b4571.png")
        );
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Mods/WTT-Campaigns.Assets/Fonts/Bender.ttf");
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
        room.LoadImage(File.ReadAllBytes(File.Exists(compactRoom) ? compactRoom : Path.Combine(folder, "54cb50c76803fa8b248b4571.png")));
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
        var navigation = StoryVisitButton.CreateNavigation(
            canvas.transform,
            font,
            () => selected = "buy",
            () => selected = "sell",
            _ => { }
        );
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
            File.WriteAllBytes(Path.Combine(folder, "visit-" + size.x + "x" + size.y + "-" + suffix + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            RenderTexture.active = null;
        }
        panel.Set(
            "Prapor",
            "This closing line waits until you continue.",
            "",
            new[]
            {
                new StoryReplyView { Id = "continue", Text = "Continue" },
            },
            false
        );
        Layout();
        Find("BUY").onClick.Invoke();
        Check(selected == "buy", "Visit Buy navigation failed");
        Find("SELL").onClick.Invoke();
        Check(selected == "sell", "Visit Sell navigation failed");
        Check(Find("Continue").interactable, "Text-only Continue is inaccessible");
        Check(
            canvas.GetComponentsInChildren<Text>().Any(t => t.text == "This closing line waits until you continue."),
            "Closing text is not visible while awaiting acknowledgement"
        );
        Find("Continue").onClick.Invoke();
        Check(selected == "continue", "Continue callback does not reach presentation");
        Capture("continue");
        panel.Set("Prapor", "", "", replies, false);
        Layout();
        Check(!canvas.GetComponentsInChildren<Button>().Any(b => b.name == "Skip playback"), "Idle visit exposes Skip");
        Check(((RectTransform)canvas.transform.Find("Story conversation")).rect.height < 210, "Empty dialogue wastes vertical space");
        Find("Want to trade?").onClick.Invoke();
        Check(selected == "trade", "Trade navigation callback failed");
        Capture("idle");
        Find("Want to trade?").OnPointerEnter(new PointerEventData(null));
        Capture("reply-hover");
        Find("Want to trade?").OnPointerExit(new PointerEventData(null));
        panel.SetBusy(true, true);
        Capture("reply-disabled");
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
        var scroll = canvas.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "Dialogue scroll");
        Check(scroll.content.rect.height > scroll.viewport.rect.height, "Long dialogue does not scroll");
        var replyScroll = canvas.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "Replies scroll");
        Check(replyScroll.viewport.rect.height > 50, "Long history hides reply viewport");
        var panelRect = (RectTransform)canvas.transform.Find("Story conversation");
        Check(panelRect.rect.height <= ((RectTransform)canvas.transform).rect.height * .5f, "Dialogue exceeds the available screen height");
        Check(panelRect.rect.width <= ((RectTransform)canvas.transform).rect.width - 32, "Dialogue exceeds screen width");
        Capture("long-dialogue");
        panel.Set(
            "Prapor",
            "Pick a reply.",
            "",
            Enumerable
                .Range(0, 24)
                .Select(i => new StoryReplyView
                {
                    Id = "reply" + i,
                    Text = "A long reply with explicit line breaks\nthat must remain readable and selectable " + i,
                })
                .ToArray(),
            false
        );
        Layout();
        replyScroll = canvas.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "Replies scroll");
        Check(replyScroll.content.rect.height > replyScroll.viewport.rect.height, "Many replies do not scroll");
        foreach (var label in replyScroll.content.GetComponentsInChildren<Text>())
            Check(label.preferredHeight <= label.rectTransform.rect.height + 1, "Reply text clips after applying the Tarkov style");
        replyScroll.verticalNormalizedPosition = 0;
        Layout();
        Find("A long reply with explicit line breaks\nthat must remain readable and selectable 23").onClick.Invoke();
        Check(selected == "reply23", "Last reply callback is inaccessible");
        Capture("many-replies");
        panel.Set("Prapor", "A sample line used to check the dialogue layout.", "", replies, false);
        Check(Find("Leave  [ESC]") is StoryVisitButton, "Leave retains placeholder styling");
        Check(Find("Leave  [ESC]").targetGraphic is Image leaveImage && leaveImage.sprite, "Leave lost Tarkov artwork");
        Capture("dialogue");
        panelRect.gameObject.SetActive(false);
        navigation.gameObject.SetActive(false);
        backdrop.gameObject.SetActive(false);
        var bar = UiElements.Rect("Trader header", canvas.transform, 540, 36);
        var ui = new UiElements(font);
        var buy = (RectTransform)ui.Button(bar, "BUY", 270, -135, 0, () => { }, 36).transform;
        var sell = (RectTransform)ui.Button(bar, "SELL", 270, 135, 0, () => { }, 36).transform;
        var visitCount = 0;
        var row = StoryTradeTabRow.Create(buy, sell, font, () => selected = "buy", () => selected = "sell", () => visitCount++, _ => { });
        row.SetState(true, true, true, true);
        Layout();
        Check(Math.Abs(buy.rect.width - 270) < 1 && Math.Abs(sell.rect.width - 270) < 1, "Native geometry was squeezed");
        Check(!buy.gameObject.activeSelf && !sell.gameObject.activeSelf, "Native visuals still overlap the replacement row");
        var visit = row.GetComponentsInChildren<StoryVisitButton>().Single(b => b.name == "Campaign Visit");
        Check(
            Math.Abs(((RectTransform)visit.transform).rect.width - (540 + 50) / 3f) < 1,
            "Connected tabs do not share the native row footprint"
        );
        visit.onClick.Invoke();
        Check(visitCount == 1, "Visit callback failed");
        Capture("button-idle");
        visit.OnPointerEnter(new PointerEventData(null));
        Capture("button-hover");
        visit.SetSelected(true);
        Capture("button-selected");
        // Reproduce the cramped native trade column, including fixed-width
        // children that previously overlapped when their parents were resized.
        buy.sizeDelta = sell.sizeDelta = new Vector2(165, 36);
        visit.SetSelected(false);
        visit.OnPointerExit(new PointerEventData(null));
        buy.anchoredPosition = new Vector2(-82.5f, 0);
        sell.anchoredPosition = new Vector2(82.5f, 0);
        row.RefreshLayout();
        Layout();
        foreach (var button in row.GetComponentsInChildren<StoryVisitButton>())
        {
            var label = button.GetComponentInChildren<Text>();
            Check(label.preferredWidth <= label.rectTransform.rect.width + 1, "Narrow tab label clips: " + label.text);
            Check(button.targetGraphic is Image image && image.sprite, "Tab still uses placeholder artwork");
            Check(button.GetComponentsInChildren<Image>().Any(i => i.type == Image.Type.Tiled), "Native tab texture is stretched");
            Check(button.GetComponentsInChildren<Image>().Any(i => i.name == "Dialogue icon" && i.sprite), "Tab icon is missing");
        }
        var tabRects = row.GetComponentsInChildren<StoryVisitButton>()
            .Select(b => (RectTransform)b.transform)
            .OrderBy(r => r.anchoredPosition.x)
            .ToArray();
        Check(
            Math.Abs(tabRects[0].anchoredPosition.x + tabRects[0].rect.width - tabRects[1].anchoredPosition.x - 25) < 1,
            "Native connected tab overlap is missing"
        );
        Capture("button-narrow");
        row.gameObject.SetActive(false);
        Check(buy.gameObject.activeSelf && sell.gameObject.activeSelf, "Native tabs are not restored on disable");
        File.WriteAllText(
            Path.Combine(folder, "visit-checks-" + size.x + "x" + size.y + ".txt"),
            "Passed: text-only Continue, closing text acknowledgement, compact idle, navigation, busy state, skip, confirmation, history, leave, long-text scrolling, screen bounds, Visit callback and hover.\n"
        );
        camera.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(room);
        Debug.Log("Visit UI checks passed at " + size);
    }
}
