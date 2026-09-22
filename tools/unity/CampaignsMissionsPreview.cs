using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using Object = UnityEngine.Object;

// Offline SDK preview: renders the actual view and checks its clipped pointer targets.
public static class CampaignsMissionsPreview
{
    private static int _checks;

    public static void Render()
    {
        foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080) })
            RenderAt(size);
        Debug.Log("PASS " + _checks + " mission selection geometry and pointer checks");
    }

    private static void Check(bool valid, string message)
    {
        if (!valid)
            throw new InvalidOperationException(message);
        _checks++;
    }

    private static void RenderAt(Vector2Int size)
    {
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/artifacts/mission-selection-preview"));
        Directory.CreateDirectory(output);
        var events = new GameObject("MissionPreviewEvents", typeof(EventSystem));
        var eventSystem = events.GetComponent<EventSystem>();
        typeof(EventSystem)
            .GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(eventSystem, null);
        var cameraObject = new GameObject("MissionPreviewCamera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        camera.cullingMask = 1 << 5;
        var target = new RenderTexture(size.x, size.y, 24);
        camera.targetTexture = target;
        var canvasObject = new GameObject(
            "MissionPreviewCanvas",
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
        var view = new MissionsScreen(
            canvas.transform,
            AssetDatabase.LoadAssetAtPath<Font>("Assets/Mods/WTT-Campaigns.Assets/Fonts/Bender.ttf")
        );
        try
        {
            var invoked = "";
            view.CloseRequested = () => invoked = "back";
            view.DeployRequested = id => invoked = "deploy:" + id;
            view.ResumeRequested = id => invoked = "resume:" + id;
            view.CancelRequested = id => invoked = "cancel:" + id;
            view.RetryRequested = () => invoked = "retry";
            view.Open();
            var missions = Enumerable
                .Range(0, 8)
                .Select(i => new MissionEntry
                {
                    Id = i.ToString(),
                    Name = "MISSION " + (i + 1),
                    Location = "Customs",
                    Briefing = "Reach the checkpoint, complete the mission objectives, and extract from the marked exit.",
                    Status = "Available",
                    Unlocked = true,
                    CanDeploy = true,
                })
                .ToArray();
            missions[1].CanDeploy = false;
            missions[1].CanResume = missions[1].CanCancel = missions[1].Active = true;
            missions[2].Completed = missions[2].CanReplay = true;
            missions[3].Unlocked = missions[3].CanDeploy = false;
            missions[3].FailureReason = "Complete the preceding mission to unlock this operation.";
            view.SetState(missions);
            Refresh();
            var scroll = view.Root.GetComponentInChildren<ScrollRect>();
            Check(scroll.content.rect.height > scroll.viewport.rect.height, "Long lists retain scrollable row heights");
            var viewport = Bounds(scroll.viewport);
            Check(
                viewport.xMin >= 0 && viewport.xMax <= size.x && viewport.yMin >= 0 && viewport.yMax <= size.y,
                "List fits screen " + size
            );
            foreach (RectTransform row in scroll.content)
            {
                foreach (var child in row.GetComponentsInChildren<RectTransform>().Where(t => t != row))
                {
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(row, child);
                    Check(
                        bounds.min.x >= row.rect.xMin - .1f
                            && bounds.max.x <= row.rect.xMax + .1f
                            && bounds.min.y >= row.rect.yMin - .1f
                            && bounds.max.y <= row.rect.yMax + .1f,
                        "Control inside " + row.name + "/" + child.name
                    );
                }
            }
            Capture("list-top");
            Click("DEPLOY", "deploy:0");
            Click("RESUME", "resume:1");
            Click("CANCEL", "cancel:1");
            Click("REPLAY", "deploy:2");
            Click("BACK", "back");
            scroll.verticalNormalizedPosition = 0;
            Refresh();
            Click("DEPLOY", "deploy:7", last: true);
            Capture("list-bottom");
            view.SetState(Array.Empty<MissionEntry>());
            Refresh();
            Check(
                view.Root.GetComponentsInChildren<Text>().Any(t => t.name == "MissionEmpty" && t.rectTransform.rect.height > 0),
                "Empty message has visible geometry"
            );
            Click("BACK", "back");
            Capture("empty");
            view.ShowMessage("Could not load missions. Please retry.", true, true);
            Refresh();
            Click("RETRY", "retry");
            Click("BACK", "back");
            view.SetBusy(true, "Loading missions…");
            Refresh();
            Click("BACK", "back");
            Capture("loading");

            void Refresh()
            {
                foreach (var t in canvasObject.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = 5;
                Canvas.ForceUpdateCanvases();
                view.Fit();
                Canvas.ForceUpdateCanvases();
                camera.Render();
            }

            Rect Bounds(RectTransform rect)
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var points = corners.Select(c => RectTransformUtility.WorldToScreenPoint(camera, c)).ToArray();
                return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
            }

            void Click(string caption, string expected, bool last = false)
            {
                var buttons = view.Root.GetComponentsInChildren<Button>().Where(b => b.name == caption);
                var button = last ? buttons.Last() : buttons.First();
                var point = Bounds((RectTransform)button.transform).center;
                var pointer = new PointerEventData(eventSystem) { position = point, button = PointerEventData.InputButton.Left };
                var hits = new List<RaycastResult>();
                canvasObject.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Check(
                    hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                    "Pointer reaches "
                        + expected
                        + " at "
                        + size
                        + " point="
                        + point
                        + " hits="
                        + string.Join(",", hits.Select(hit => hit.gameObject.name))
                );
                invoked = "";
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Check(invoked == expected, "Click invokes " + expected);
            }

            void Capture(string name)
            {
                foreach (var text in view.Root.GetComponentsInChildren<Text>())
                    text.font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
                foreach (var graphic in view.Root.GetComponentsInChildren<Graphic>())
                    graphic.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(output, name + "-" + size.x + "x" + size.y + ".png"), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                RenderTexture.active = previous;
            }
        }
        finally
        {
            view.Dispose();
            Object.DestroyImmediate(canvasObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(events);
        }
    }
}
