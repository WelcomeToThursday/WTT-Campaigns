using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeasonalPerks.UI.BattlePass;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Screens;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SeasonalHubPreview
{
    private const string Root = "Assets/Mods/SeasonalPerks.Assets";

    [MenuItem("SDK/Seasonal Perks/Render season hub previews")]
    public static void Render()
    {
        var previous = PlayerSettings.colorSpace;
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            RenderGamma();
        }
        finally
        {
            PlayerSettings.colorSpace = previous;
        }
    }

    private static void RenderGamma()
    {
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks"));
        var output = Path.Combine(project, "Research/UI");
        Directory.CreateDirectory(output);
        var raw = File.ReadAllText(Path.Combine(project, "data/hub.json"));
        var catalogue = JObject.Parse(File.ReadAllText(Path.Combine(project, "data/catalogue.json")));
        var locale = JObject.Parse(File.ReadAllText(Path.Combine(project, "data/locales/en.json")));
        var perks = new List<PerkEntry>();
        var icons = new Dictionary<string, string>();
        foreach (var group in new[] { "common", "personal" })
        foreach (JObject perk in catalogue[group])
        {
            var id = (string)perk["id"];
            perks.Add(
                new PerkEntry
                {
                    Id = id,
                    Name = (string)locale[id + " name"],
                    Description = (string)locale[id + " description"],
                    Common = group == "common",
                    Points = (int?)perk["points"] ?? 0,
                }
            );
            icons[id] = Root + "/Icons/" + Path.GetFileName((string)perk["imageUrl"]);
        }
        foreach (var resolution in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1902, 992) })
        {
            var data = JsonUtility.FromJson<HubState>(raw);
            var checks = new List<string>();
            void Check(bool valid, string label)
            {
                if (!valid)
                    throw new Exception(label);
                checks.Add(label);
            }
            var cameraObject = new GameObject("HubPreviewCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.cullingMask = 1 << 5;
            var target = new RenderTexture(resolution.x, resolution.y, 24);
            camera.targetTexture = target;
            var canvasObject = new GameObject(
                "HubPreviewCanvas",
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
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var sprites = new Dictionary<string, Sprite>();
            Sprite Load(string path)
            {
                if (sprites.TryGetValue(path, out var sprite))
                    return sprite;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                    throw new Exception("Invalid image " + path);
                return sprites[path] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            }
            var view = new SeasonsHubScreen(
                canvasObject.transform,
                AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Bender.ttf"),
                name => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/HubArtwork/" + name + ".png")
            );
            view.ImageRequested = (id, image) =>
            {
                image.sprite = Load(Root + "/HubImages/" + id + ".png");
                image.color = Color.white;
            };
            view.PerkIconRequested = (id, image) =>
            {
                image.sprite = Load(icons[id]);
                image.color = Color.white;
            };
            var events = new GameObject("HubPreviewEvents", typeof(EventSystem));
            var pointer = new PointerEventData(events.GetComponent<EventSystem>());
            void Capture(string name)
            {
                foreach (var t in canvasObject.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = 5;
                Canvas.ForceUpdateCanvases();
                view.Fit();
                Canvas.ForceUpdateCanvases();
                foreach (var t in canvasObject.GetComponentsInChildren<Text>())
                    t.font.RequestCharactersInTexture(t.text, t.fontSize, t.fontStyle);
                foreach (var g in canvasObject.GetComponentsInChildren<Graphic>())
                    g.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                var texture = new Texture2D(resolution.x, resolution.y, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(output, name + "-" + resolution.y + ".png"), texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                RenderTexture.active = previous;
            }
            view.Open();
            view.SetState(data, perks.ToArray());
            Capture("hub-first");
            view.SelectReward(1);
            Capture("hub-tarcoins");
            view.ChangePage(-1);
            Check(view.PageIndex == 0, "First page boundary");
            for (var p = 0; p < data.Pages.Length; p++)
            {
                Check(view.PageIndex == p, "Page navigation " + p);
                for (var r = 0; r < data.Pages[p].Rewards.Length; r++)
                {
                    view.SelectReward(r);
                    Check(view.SelectedRewardIndex == r, "Reward selection " + p + "/" + r);
                    Check(
                        view.Root.GetComponentsInChildren<Image>().Any(i => i.name == "RewardFullImage" && i.sprite),
                        "Reward image " + p + "/" + r
                    );
                }
                if (p == 1)
                {
                    view.SelectReward(3);
                    Capture("hub-page2");
                }
                if (p == 9)
                {
                    view.SelectReward(0);
                    Capture("hub-voice");
                }
                view.ChangePage(1);
            }
            Check(view.PageIndex == 11, "Last page boundary");
            view.ShowTab(HubTab.SeasonalRewards);
            Capture("hub-seasonal");
            for (var i = 0; i < data.SeasonalRewards.Length; i++)
                view.SelectReward(i);
            view.ShowTab(HubTab.AboutSeason);
            Capture("hub-about");
            var modifier = view.Root.GetComponentsInChildren<HubPointer>().First(p => p.name.StartsWith("Modifier-"));
            modifier.OnPointerEnter(pointer);
            Capture("hub-tooltip");
            Check(view.Root.GetComponentsInChildren<Transform>().Any(t => t.name == "HubTooltip"), "Modifier tooltip opens");
            modifier.OnPointerExit(pointer);
            Check(!view.Root.GetComponentsInChildren<Transform>().Any(t => t.name == "HubTooltip"), "Modifier tooltip closes");
            for (var i = 1; i < data.Slides.Length; i++)
            {
                view.ChangePage(1);
                Check(view.SlideIndex == i, "Carousel page " + i);
            }
            Capture("hub-carousel-last");
            view.ChangePage(1);
            Check(view.SlideIndex == 4, "Carousel end boundary");
            view.ShowTab(HubTab.BattlePass);
            Check(view.PageIndex == 11, "Battle Pass page survives tab switching");
            var unavailable = view
                .Root.GetComponentsInChildren<Button>()
                .Where(b => b.name.Contains("DOCUMENTS") || b.name == "CLAIM REWARD")
                .ToArray();
            Check(unavailable.Length == 3 && unavailable.All(b => !b.interactable), "All transaction buttons unavailable");
            unavailable[0].GetComponent<HubPointer>().OnPointerEnter(pointer);
            Capture("hub-unavailable");
            view.Close();
            Check(!view.Root.activeSelf, "Close hides hub");
            view.Open();
            view.SetState(data, perks.ToArray());
            Check(view.PageIndex == 0 && view.SelectedRewardIndex == 0, "Fresh opening resets selection");
            var tutorialCompletions = 0;
            view.TutorialCompleted = () => tutorialCompletions++;
            view.ChangePage(1);
            view.SelectReward(1);
            var tutorialPage = view.PageIndex;
            var tutorialSelection = view.SelectedRewardIndex;
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "BattlePassTutorial").onClick.Invoke();
            Check(view.HasTutorial && view.TutorialStep == 0, "Info button starts the tutorial after page navigation");
            view.PreviousTutorialStep();
            Check(view.TutorialStep == 0, "Tutorial first-step boundary");
            view.ChangePage(1);
            view.SelectReward(0);
            view.ShowTab(HubTab.SeasonalRewards);
            Check(
                view.PageIndex == tutorialPage && view.SelectedRewardIndex == tutorialSelection && view.Tab == HubTab.BattlePass,
                "Tutorial blocks underlying paging, selection and tab navigation"
            );
            for (var step = 0; step < 8; step++)
            {
                Check(view.HasTutorial && view.TutorialStep == step, "Tutorial reaches step " + (step + 1));
                Capture("hub-tutorial-" + (step + 1));
                var overlay = view.Root.GetComponentsInChildren<Transform>().Single(t => t.name == "BattlePassTutorialOverlay");
                foreach (var label in overlay.GetComponentsInChildren<Text>())
                    Check(
                        label.preferredHeight <= label.rectTransform.rect.height + 2,
                        "Tutorial step " + (step + 1) + " text fits: " + label.transform.parent.name
                    );
                var pageGroup = view.Root.GetComponentsInChildren<CanvasGroup>().Single(g => g.name == "HubPage");
                Check(!pageGroup.interactable && !pageGroup.blocksRaycasts, "Tutorial blocks underlying UI at step " + (step + 1));
                if (step == 3)
                    Check(
                        overlay.GetComponentsInChildren<Transform>().Any(t => t.name == "TutorialDocumentInfo"),
                        "Document information is shown during step four"
                    );
                if (step < 7)
                    view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "NEXT").onClick.Invoke();
            }
            view.PreviousTutorialStep();
            Check(view.TutorialStep == 6, "Final briefing can return to exchange guidance");
            view.AdvanceTutorial();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "FINISH").onClick.Invoke();
            Check(!view.HasTutorial && tutorialCompletions == 1, "Finishing closes tutorial and reports completion once");
            Check(
                view.PageIndex == tutorialPage && view.SelectedRewardIndex == tutorialSelection,
                "Tutorial preserves the previously selected reward and page"
            );
            Check(
                view.Root.GetComponentsInChildren<CanvasGroup>().Single(g => g.name == "HubPage").interactable,
                "Finishing restores underlying controls"
            );
            view.AdvanceTutorial();
            Check(tutorialCompletions == 1, "Advancing a closed tutorial has no effect");
            view.StartTutorial();
            Check(view.TutorialStep == 0, "Completed tutorial can be replayed from the beginning");
            view.DismissTutorial();
            Check(!view.HasTutorial && tutorialCompletions == 1, "Escape-style dismissal does not mark tutorial completed");
            view.StartTutorial();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "SKIP TUTORIAL").onClick.Invoke();
            Check(!view.HasTutorial && tutorialCompletions == 2, "Explicit skip reports the tutorial seen");
            view.StartTutorial();
            view.SetState(data, perks.ToArray());
            Check(!view.HasTutorial, "State refresh cleans up tutorial overlay");
            view.StartTutorial();
            view.Close();
            Check(!view.HasTutorial, "Closing the hub cleans up tutorial overlay");
            view.StartTutorial();
            Check(!view.HasTutorial, "Tutorial cannot start on a closed hub");
            view.Open();
            view.StartTutorial();
            Check(!view.HasTutorial, "Tutorial cannot start while loading");
            view.SetState(data, perks.ToArray());
            data.Pages[0].Rewards[0].Claimed = true;
            data.Documents[0].Count = 2;
            view.SetState(data, perks.ToArray());
            Capture("hub-synthetic-owned");
            Check(view.Root.GetComponentsInChildren<Text>().Any(t => t.text == "CLAIMED"), "Synthetic claimed state");
            data.PreviewOnly = false;
            data.Revision = 7;
            data.UniversalCount = 3;
            data.ExchangeRate = 5;
            data.CrateCost = 10;
            data.CrateUnavailableReason = "The season crate contents have not been recovered.";
            foreach (var document in data.Documents)
                document.Count = 10;
            var claim = data.Pages[0].Rewards[0];
            claim.Claimed = false;
            claim.CanClaim = true;
            claim.UniversalNeeded = 1;
            data.Documents.First(d => d.Id == claim.Costs[0].DocumentId).Count = 0;
            data.RemainingDocuments = 30;
            view.SetState(data, perks.ToArray());
            HubAction transaction = null;
            view.TransactionRequested = action => transaction = action;
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "CLAIM REWARD").onClick.Invoke();
            Check(view.HasDialog, "Claim confirmation opens");
            view.StartTutorial();
            Check(!view.HasTutorial && view.HasDialog, "Tutorial cannot replace a transaction confirmation");
            Capture("hub-claim-confirm");
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "CONFIRM").onClick.Invoke();
            Check(
                transaction != null && transaction.RewardId == claim.Id && transaction.UseClassified && transaction.ExpectedRevision == 7,
                "Classified confirmation callback and revision"
            );
            Check(!view.HasDialog, "Confirmation dismisses before transaction");
            transaction = null;
            view.StartTutorial();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "CLAIM REWARD").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE DOCUMENTS").onClick.Invoke();
            Check(view.HasTutorial && !view.HasDialog && transaction == null, "Tutorial cannot trigger claims or exchanges");
            view.DismissTutorial();
            foreach (var document in data.Documents)
            {
                document.Count = 10;
            }
            var referenceCounts = new[] { 1, 0, 0, 0, 4, 2, 1, 0 };
            for (var i = 0; i < data.Documents.Length; i++)
                data.Documents[i].Count = referenceCounts[i % referenceCounts.Length];
            view.SetState(data, perks.ToArray());
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE DOCUMENTS").onClick.Invoke();
            Check(view.HasDialog, "Exchange dialog opens");
            Capture("hub-exchange-empty");
            Check(
                !view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").interactable,
                "Exchange requires sources and an explicit output"
            );
            Check(
                !view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeSource1").interactable,
                "Unowned source documents cannot be selected"
            );
            foreach (var document in data.Documents)
                document.Count = 10;
            view.SetState(data, perks.ToArray());
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE DOCUMENTS").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "DOCUMENTS").onClick.Invoke();
            for (var i = 0; i < 5; i++)
                view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeSource0").onClick.Invoke();
            Check(
                !view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").interactable,
                "Sources alone do not choose an output"
            );
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "SelectedSource0").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeSource1").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeTarget2").onClick.Invoke();
            Capture("hub-exchange");
            Check(
                view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").interactable,
                "Exact ordinary-document quantity enables exchange"
            );
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").onClick.Invoke();
            Check(
                transaction.Action == "exchange"
                    && transaction.Sources.Values.Sum() == 5
                    && !transaction.UseClassified
                    && transaction.Sources[data.Documents[0].Id] == 4
                    && transaction.Sources[data.Documents[1].Id] == 1
                    && transaction.DocumentId == data.Documents[2].Id
                    && transaction.ExpectedRevision == 7,
                "Exchange callback preserves mixed sources, selected output and revision"
            );
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE DOCUMENTS").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "CONTAINER").onClick.Invoke();
            for (var i = 0; i < 10; i++)
                view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeSource0").onClick.Invoke();
            Capture("hub-exchange-container-locked");
            Check(
                !view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").interactable,
                "Unavailable container remains locked at the exact cost"
            );
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeBack").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "DOCUMENTS").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeTarget0").onClick.Invoke();
            Check(
                !view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").interactable,
                "Returning from container mode clears sources above the document cost"
            );
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "CloseExchange").onClick.Invoke();
            Check(!view.HasDialog, "Exchange title-bar close dismisses dialog");
            data.CrateUnavailableReason = "";
            view.SetState(data, perks.ToArray());
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE DOCUMENTS").onClick.Invoke();
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "CONTAINER").onClick.Invoke();
            for (var i = 0; i < 10; i++)
                view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "ExchangeSource0").onClick.Invoke();
            Check(
                view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").interactable,
                "Available container enables at the exact cost"
            );
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").onClick.Invoke();
            Check(
                transaction.Crate && transaction.DocumentId == "" && transaction.Sources.Values.Sum() == 10,
                "Container callback does not require a document target"
            );
            view.ShowResult("Reward added to your stash.");
            Capture("hub-result");
            Check(view.HasDialog, "Result dialog opens");
            view.Close();
            Check(!view.HasDialog, "Closing releases transaction dialog");
            view.Open();
            view.SetState(data, perks.ToArray());
            view.ShowMessage("Unable to load the season. Check the local server and try again.", true);
            Capture("hub-error");
            var retried = false;
            view.RetryRequested = () => retried = true;
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "RETRY").onClick.Invoke();
            Check(retried, "Error retry callback");
            File.WriteAllText(
                Path.Combine(output, "hub-checks-" + resolution.y + ".json"),
                new JObject { ["passed"] = checks.Count, ["checks"] = new JArray(checks) }.ToString()
            );
            view.Dispose();
            foreach (var sprite in sprites.Values)
            {
                Object.DestroyImmediate(sprite.texture);
                Object.DestroyImmediate(sprite);
            }
            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(canvasObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(events);
        }
        Debug.Log("Season hub: all reward and navigation checks passed; Gamma previews and transaction fixtures rendered.");
    }
}
