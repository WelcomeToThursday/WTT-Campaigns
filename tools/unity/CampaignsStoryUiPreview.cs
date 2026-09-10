using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Media;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using Object = UnityEngine.Object;

namespace WTT.Campaigns.Tools;

public static class CampaignsStoryUiPreview
{
    public static void Render()
    {
        foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1902, 992) })
        {
            RenderAt(size);
        }
    }

    private static void RenderAt(Vector2Int size)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/Story/Preview"));
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool valid, string message)
        {
            if (!valid)
            {
                throw new InvalidOperationException(message);
            }
            checks.Add(message);
        }
        var camera = new GameObject("Story UI camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 5;
        var target = new RenderTexture(size.x, size.y, 24);
        camera.targetTexture = target;
        var canvas = new GameObject("Story UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1800, 980);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        // Match beta level44 TasksPart: a VerticalLayoutGroup with expansion
        // disabled and a flexible TasksPanel. A standalone fixed rect hides the
        // zero-size regression seen in the installed game.
        var tasksPart = UiElements.Rect("TasksPart", canvas.transform, 0, 0);
        UiElements.Stretch(tasksPart, 36, 620, 116, 50);
        var nativeLayout = tasksPart.gameObject.AddComponent<VerticalLayoutGroup>();
        nativeLayout.childControlWidth = nativeLayout.childControlHeight = true;
        nativeLayout.childForceExpandWidth = nativeLayout.childForceExpandHeight = false;
        var native = UiElements.Rect("Native TasksPanel", tasksPart, 1261, 905);
        var nativeSize = native.gameObject.AddComponent<LayoutElement>();
        nativeSize.flexibleWidth = nativeSize.flexibleHeight = 1;
        Canvas.ForceUpdateCanvases();
        var expectedSize = native.rect.size;
        var root = StoryTaskLayout.CreatePanel(native);
        native.gameObject.SetActive(false);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tasksPart);
        Check(Vector2.Distance(root.rect.size, expectedSize) < .1f, "Journal occupies the native task panel's full layout slot");
        Check(root.rect.width > 800 && root.rect.height > 650, "Native parent cannot collapse the journal");
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Mods/WTT-Campaigns.Assets/Fonts/Bender.ttf");
        var reads = new HashSet<string>();
        // Use an existing room render only as sample chapter artwork; no sample
        // chapter data or image is installed into the user's season.
        var artTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        artTexture.LoadImage(File.ReadAllBytes(Path.Combine(output, "5a7c2eca46aef81a7ca2145d.png")));
        var sampleWidth = artTexture.width * .4f;
        var sampleHeight = sampleWidth * 150f / (expectedSize.x - 274);
        var sampleArt = Sprite.Create(artTexture, new Rect(0, artTexture.height * .55f, sampleWidth, sampleHeight), new Vector2(.5f, .5f));
        var panel = new StoryJournalPanel(
            root,
            font,
            id => id == "preview-art" ? sampleArt : null,
            (kind, id) => reads.Add(kind + ":" + id),
            _ => { }
        );
        var exampleObjectives = new[]
        {
            "Locate the research equipment",
            "Deliver the recovered samples",
            "Inspect the service entrance",
            "Find the storage records",
            "Reach the extraction point",
            "Secure the damaged transmitter",
            "Identify the shipment's destination",
            "Search the maintenance workshop",
            "Recover the missing tools",
            "Check the loading bay",
        };
        var chapter = new StoryChapterView
        {
            Id = "chapter",
            Name = "Field research",
            Image = "preview-art",
            Status = "Active",
            Unread = true,
            Notes = new[]
            {
                new StoryNoteView
                {
                    Id = "note",
                    Text = "The test delivery reached the depot. I can check the remaining objectives before setting out again.",
                    Unread = true,
                },
            },
            Objectives = Enumerable
                .Range(0, 24)
                .Select(i => new StoryObjectiveView
                {
                    Id = "objective" + i,
                    Text = i < exampleObjectives.Length ? exampleObjectives[i] : "Inspect storage zone " + (i + 1),
                    Main = i < 20,
                    Hint = i % 3 == 0 ? "Only equipment from this season counts toward the delivery." : "",
                    Counter = i == 1 ? "2 / 5" : "",
                    Unread = true,
                    Complete = i == 4,
                    Failed = i == 5,
                })
                .ToArray(),
            Links = new[]
            {
                new StoryLinkView
                {
                    Id = "link",
                    Name = "Research equipment",
                    Unread = true,
                },
            },
        };
        panel.SetState(new[] { chapter });
        void Layout()
        {
            for (var i = 0; i < 4; i++)
            {
                foreach (var transform in canvas.GetComponentsInChildren<Transform>(true))
                {
                    transform.gameObject.layer = 5;
                }
                Canvas.ForceUpdateCanvases();
                panel.Fit();
            }
        }
        panel.SetState(Array.Empty<StoryChapterView>());
        Layout();
        var empty = root.GetComponentsInChildren<Text>().Single(t => t.name == "Empty story");
        Check(
            !root.GetComponentsInChildren<Text>().Any(t => t.name == "Chapter name"),
            "Empty snapshots remove the previous chapter content"
        );
        var emptyBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(root, empty.transform);
        Check(
            root.rect.Contains(emptyBounds.min) && root.rect.Contains(emptyBounds.max),
            "Empty story text remains inside the native task area"
        );
        Check(empty.alignment == TextAnchor.MiddleCenter, "Empty story text is centered");
        void Capture(string name)
        {
            foreach (var label in root.GetComponentsInChildren<Text>())
                label.font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var capture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            capture.Apply();
            File.WriteAllBytes(Path.Combine(output, name + "-" + size.y + ".png"), capture.EncodeToPNG());
            if (name == "journal")
            {
                var corners = new Vector3[4];
                root.GetWorldCorners(corners);
                var lower = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                var upper = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                var detail = new Texture2D(
                    Mathf.RoundToInt(upper.x - lower.x),
                    Mathf.RoundToInt(upper.y - lower.y),
                    TextureFormat.RGB24,
                    false
                );
                detail.ReadPixels(new Rect(Mathf.RoundToInt(lower.x), Mathf.RoundToInt(lower.y), detail.width, detail.height), 0, 0);
                detail.Apply();
                File.WriteAllBytes(Path.Combine(output, "journal-detail-" + size.y + ".png"), detail.EncodeToPNG());
                Object.DestroyImmediate(detail);
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(capture);
        }
        Capture("journal-empty");
        // Exercise the same active-object replacement used by Story / Side /
        // Operational, including returning to Story after a size change.
        root.gameObject.SetActive(false);
        native.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tasksPart);
        Check(Vector2.Distance(native.rect.size, expectedSize) < .1f, "Switching away restores the native task layout");
        tasksPart.offsetMax += new Vector2(-120, -60);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tasksPart);
        expectedSize = native.rect.size;
        native.gameObject.SetActive(false);
        root.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tasksPart);
        Layout();
        Check(Vector2.Distance(root.rect.size, expectedSize) < .1f, "Returning to Story follows the resized task area");
        tasksPart.offsetMax += new Vector2(120, 60);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tasksPart);
        panel.SetState(new[] { chapter });
        Layout();
        Check(reads.Contains("note:note"), "Visible note is marked read");
        Check(reads.Contains("condition:objective0"), "Visible objective is marked read");
        Check(!reads.Contains("condition:objective23"), "Offscreen objective remains unread");
        var objectives = root.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "Chapter objectives");
        objectives.verticalNormalizedPosition = 0;
        Layout();
        Debug.Log(
            "Journal bottom scroll: content="
                + objectives.content.rect
                + "; viewport="
                + objectives.viewport.rect
                + "; position="
                + objectives.verticalNormalizedPosition
                + "; last="
                + RectTransformUtility.CalculateRelativeRectTransformBounds(
                    objectives.viewport,
                    objectives.content.GetChild(objectives.content.childCount - 1)
                )
        );
        Check(reads.Contains("condition:objective23"), "Scrolling marks the newly visible objective read");
        panel.SetState(new[] { chapter });
        Layout();
        objectives = root.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "Chapter objectives");
        Check(objectives.verticalNormalizedPosition < .01f, "Snapshot refresh preserves objective scroll position");
        objectives.verticalNormalizedPosition = 1;
        Layout();
        Capture("journal");
        var statusPanel = root.GetComponentsInChildren<Image>().Single(i => i.name == "Chapter status panel");
        Check(statusPanel.sprite == StoryUiArtwork.Load("journal-active"), "Status uses the recovered live chapter artwork");
        Check(Math.Abs(statusPanel.rectTransform.rect.width - 150) < .1f, "Status preserves the live 150-pixel panel width");
        Check(
            root.GetComponentsInChildren<Text>()
                .Where(t => t.name == "Objective text")
                .All(t => t.fontSize == 20 && t.fontStyle == FontStyle.Normal),
            "Objectives use the live 20-pixel regular typography"
        );
        Check(
            root.GetComponentsInChildren<Button>()
                .Where(b => b.name.StartsWith("Show "))
                .All(b => ((RectTransform)b.transform).rect.width == 44),
            "History and completed objectives use compact expand controls"
        );
        var history = root.GetComponentsInChildren<Button>().Single(b => b.name == "Show history");
        history.onClick.Invoke();
        Layout();
        var completed = root.GetComponentsInChildren<Button>().Single(b => b.name == "Show completed objectives");
        completed.onClick.Invoke();
        Layout();
        Check(
            root.GetComponentsInChildren<Image>().Any(i => i.name == "Objective result" && i.sprite.name == "Story journal-cross"),
            "Expanded objectives retain native failed markers"
        );
        Capture("journal-expanded");
        panel.SetState(
            new[]
            {
                chapter,
                new StoryChapterView
                {
                    Id = "completed",
                    Name = "Supply route",
                    Status = "Complete",
                },
            }
        );
        Layout();
        root.GetComponentsInChildren<Button>().Single(b => b.name == "Chapter completed").onClick.Invoke();
        Layout();
        Check(
            root.GetComponentsInChildren<Image>().Single(i => i.name == "Chapter status panel").sprite
                == StoryUiArtwork.Load("journal-complete"),
            "Chapter selection updates the recovered completion styling"
        );
        Capture("journal-completed");
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(canvas.gameObject);
        Object.DestroyImmediate(camera.gameObject);
        Object.DestroyImmediate(sampleArt);
        Object.DestroyImmediate(artTexture);
        File.WriteAllLines(Path.Combine(output, "journal-checks-" + size.y + ".txt"), checks);
        Debug.Log("Story journal: " + checks.Count + " checks passed at " + size);
    }
}
