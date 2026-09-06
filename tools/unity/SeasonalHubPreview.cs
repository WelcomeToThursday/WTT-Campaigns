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
            Capture("hub-claim-confirm");
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "CONFIRM").onClick.Invoke();
            Check(
                transaction != null && transaction.RewardId == claim.Id && transaction.UseClassified && transaction.ExpectedRevision == 7,
                "Classified confirmation callback and revision"
            );
            Check(!view.HasDialog, "Confirmation dismisses before transaction");
            foreach (var document in data.Documents)
            {
                document.Count = 10;
            }
            view.SetState(data, perks.ToArray());
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE DOCUMENTS").onClick.Invoke();
            Check(view.HasDialog, "Exchange dialog opens");
            for (var i = 0; i < 5; i++)
                view.Root.GetComponentsInChildren<Button>().First(b => b.name == "+").onClick.Invoke();
            Capture("hub-exchange");
            Check(
                view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").interactable,
                "Exact ordinary-document quantity enables exchange"
            );
            view.Root.GetComponentsInChildren<Button>().Single(b => b.name == "EXCHANGE").onClick.Invoke();
            Check(
                transaction.Action == "exchange" && transaction.Sources.Values.Sum() == 5 && !transaction.UseClassified,
                "Exchange callback contains ordinary sources only"
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
