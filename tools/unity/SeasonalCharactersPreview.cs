using System;
using System.IO;
using System.Linq;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Profiles;
using SeasonalPerks.UI.Screens;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class SeasonalCharactersPreview
{
    private const string Assets = "Assets/Mods/SeasonalPerks.Assets";
    private static int _checks;

    private static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
        _checks++;
    }

    public static void Render()
    {
        var colorSpace = PlayerSettings.colorSpace;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        try
        {
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720), new Vector2Int(2560, 1080) })
                RenderAt(size);
            Debug.Log("PASS " + _checks + " character carousel interaction checks");
        }
        finally
        {
            PlayerSettings.colorSpace = colorSpace;
        }
    }

    private static void RenderAt(Vector2Int size)
    {
        var events = new GameObject("Events", typeof(EventSystem));
        typeof(EventSystem)
            .GetMethod("OnEnable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(events.GetComponent<EventSystem>(), null);
        var cameraObject = new GameObject("Camera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        camera.cullingMask = 1 << 5;
        var target = new RenderTexture(size.x, size.y, 24);
        camera.targetTexture = target;
        var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1800, 980);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var view = new SeasonalScreen(
            canvas.transform,
            name => AssetDatabase.LoadAssetAtPath<GameObject>(Assets + "/UI/" + name + ".prefab"),
            AssetDatabase.LoadAssetAtPath<Font>(Assets + "/Fonts/Bender.ttf")
        );
        view.ArtworkRequested = (name, image) =>
        {
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                Assets + "/" + (name.StartsWith("hub:") ? "HubArtwork/" + name.Substring(4) : "SelectionArtwork/" + name) + ".png"
            );
            image.enabled = image.sprite;
        };
        // Empty artwork stands in for EFT equipment models in editor previews.
        view.CharacterRequested = (id, raw) =>
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Assets + "/SelectionArtwork/seasonal-empty.png");
            raw.texture = sprite.texture;
            raw.color = Color.white;
        };
        var state = new ScreenState
        {
            ActiveMode = "seasonal",
            SelectedCharacterId = "one",
            SeasonId = "kord",
            Seasons = new[]
            {
                new SeasonEntry
                {
                    Id = "kord",
                    Name = "KORD BREACH",
                    Description = "Explore the original seasonal challenge.",
                },
                new SeasonEntry
                {
                    Id = "winter",
                    Name = "WINTER OPERATIONS",
                    Description = "A different season with its own modifiers and rewards.",
                },
            },
            Characters = new[]
            {
                new CharacterEntry
                {
                    Id = "normal",
                    Mode = "normal",
                    Exists = true,
                    Name = "Nomad",
                    Level = 32,
                },
                new CharacterEntry
                {
                    Id = "one",
                    Mode = "seasonal",
                    Exists = true,
                    Name = "Pathfinder",
                    Level = 18,
                    SeasonId = "kord",
                    SeasonName = "KORD BREACH",
                },
                new CharacterEntry
                {
                    Id = "two",
                    Mode = "seasonal",
                    Exists = true,
                    Name = "WinterFox",
                    Level = 7,
                    SeasonId = "winter",
                    SeasonName = "WINTER OPERATIONS",
                    Side = "Bear",
                },
                new CharacterEntry
                {
                    Id = "three",
                    Mode = "seasonal",
                    Exists = true,
                    Name = "SecondChance",
                    Level = 1,
                    SeasonId = "kord",
                    SeasonName = "KORD BREACH",
                },
            },
        };
        string selected = null,
            managed = null,
            seasonId = null;
        bool wiped = false;
        view.SwitchRequested = id => selected = id;
        view.CharacterManagementRequested = (id, wipe) =>
        {
            managed = id;
            wiped = wipe;
        };
        view.SeasonChosen = id => seasonId = id;
        view.SetState(state, ScreenPage.Characters);
        view.Open(ScreenPage.Characters);
        Canvas.ForceUpdateCanvases();
        view.Fit();
        var carousel = view.Root.GetComponentInChildren<ProfileCarousel>();
        Check(carousel.transform.childCount == 5, "All characters and a permanent creation card are present");
        Capture("carousel");
        var one = carousel.transform.Find("one-profile");
        var two = carousel.transform.Find("two-profile");
        var seasonalHover = one.GetComponent<ProfileCardHover>();
        var contentMask = one.Find("CardContentMask");
        Check(
            contentMask
                && contentMask.GetComponent<RectMask2D>()
                && ((RectTransform)contentMask).sizeDelta == new Vector2(390, 800)
                && seasonalHover.Info.parent == contentMask,
            "Sliding season details are clipped to the card boundary"
        );
        Check(
            one.Find("SeasonGlowIdle").parent == one && one.Find("SeasonGlowHover").parent == one,
            "Season glow remains outside the content mask"
        );
        seasonalHover.Apply(.35f);
        Capture("carousel-hover-transition");
        seasonalHover.Apply(0);
        Button Find(Transform parent, string name) => parent.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
        Find(two, "Select-seasonal").onClick.Invoke();
        Check(selected == "two", "Selection targets the specific character");
        Find(one, "DELETE").onClick.Invoke();
        Check(view.DialogOpen && managed == null, "Delete requires confirmation");
        Check(EventSystem.current.currentSelectedGameObject.name == "CANCEL", "Destructive dialog initially focuses Cancel");
        var managementWindow = view
            .Root.transform.GetComponentsInChildren<RectTransform>()
            .First(t => t.name == "CharacterManagement")
            .Find("Window");
        Check(((RectTransform)managementWindow).sizeDelta == new Vector2(1000, 546), "Management uses native confirmation dimensions");
        Check(managementWindow.Find("Caption/Title").GetComponent<Text>().fontSize == 14, "Management uses native caption typography");
        Capture("delete-confirmation");
        Find(view.Root.transform, "CANCEL").onClick.Invoke();
        Check(!view.DialogOpen && managed == null, "Cancel does not delete");
        Find(two, "WIPE").onClick.Invoke();
        Capture("wipe-confirmation");
        Find(view.Root.transform, "ACCEPT").onClick.Invoke();
        Check(managed == "two" && wiped, "Wipe confirmation targets the named character");
        managed = null;
        Find(one, "DELETE").onClick.Invoke();
        Find(view.Root.transform, "ACCEPT").onClick.Invoke();
        Check(managed == "one" && !wiped, "Delete confirmation targets the named character");
        Find(carousel.transform.Find("new-seasonal-profile"), "Select-seasonal").onClick.Invoke();
        Capture("season-choice");
        var seasonWindow = view
            .Root.transform.GetComponentsInChildren<RectTransform>()
            .First(t => t.name == "SeasonSelection")
            .Find("Window");
        Check(seasonWindow.Find("Caption/Title").GetComponent<Text>().text == "Choose a season", "Season picker uses the shared caption");
        Find(view.Root.transform, "winter").onClick.Invoke();
        Check(
            Find(view.Root.transform, "winter").transform.Find("Selected").gameObject.activeSelf
                && !Find(view.Root.transform, "kord").transform.Find("Selected").gameObject.activeSelf,
            "Selection marker follows the chosen season"
        );
        Find(view.Root.transform, "CONTINUE").onClick.Invoke();
        Check(seasonId == "winter", "Creation uses the chosen season");
        carousel.Focus(5);
        Check(carousel.Counter.text == "1 / 5", "Forward scrolling wraps");
        carousel.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -1) });
        view.SetBusy(true);
        var counter = carousel.Counter.text;
        carousel.Move(1);
        Check(carousel.Counter.text == counter, "Busy carousel does not navigate");
        view.SetBusy(false);
        state.Characters[1].Wiped = true;
        state.Characters[1].Exists = false;
        view.SetState(state, ScreenPage.Characters);
        carousel = view.Root.GetComponentInChildren<ProfileCarousel>();
        var wipedCard = carousel.transform.Find("one-profile");
        Check(wipedCard && !Find(wipedCard, "WIPE").gameObject.activeSelf, "Wiped slot remains visible without another wipe action");
        string recreated = null;
        view.RecreationRequested = (id, season) => recreated = id + ":" + season;
        Find(wipedCard, "Select-seasonal").onClick.Invoke();
        Check(recreated == "one:kord", "Recreate uses the wiped character slot and season");
        Capture("wiped-card");
        view.BeginCreation(state, "one");
        Check(view.CreationCharacterId == "one" && view.Selected.Length == 0, "Recreation starts with a fresh modifier selection");
        Check(
            view.Page == ScreenPage.CreationIdentity && view.CreationOperationId.Length == 32,
            "Additional character starts a fresh creation flow"
        );
        state.Seasons = new[]
        {
            new SeasonEntry
            {
                Id = "kord",
                Name = "Season One",
                Description = "",
            },
        };
        view.SetState(state, ScreenPage.Characters);
        carousel = view.Root.GetComponentInChildren<ProfileCarousel>();
        Find(carousel.transform.Find("new-seasonal-profile"), "Select-seasonal").onClick.Invoke();
        Capture("season-choice-single");
        Check(
            !Find(view.Root.transform, "kord").transform.Find("Description").gameObject.activeSelf,
            "Description-free season has a compact row"
        );
        Find(view.Root.transform, "CANCEL").onClick.Invoke();
        Check(seasonId == "winter", "Cancel leaves the chosen season unchanged");
        state.Seasons = Enumerable
            .Range(1, 8)
            .Select(i => new SeasonEntry
            {
                Id = "season-" + i,
                Name = "SEASON " + i,
                Description = "A separate season with its own challenges, modifiers and rewards.",
            })
            .ToArray();
        view.SetState(state, ScreenPage.Characters);
        carousel = view.Root.GetComponentInChildren<ProfileCarousel>();
        Find(carousel.transform.Find("new-seasonal-profile"), "Select-seasonal").onClick.Invoke();
        Capture("season-choice-many");
        var seasonScroll = view.Root.GetComponentsInChildren<ScrollRect>().Single(s => s.name == "AvailableSeasons");
        Check(
            seasonScroll.verticalScrollbar.gameObject.activeSelf && seasonScroll.content.rect.height > seasonScroll.viewport.rect.height,
            "Long season lists enable scrolling"
        );
        Find(view.Root.transform, "CANCEL").onClick.Invoke();
        state.Seasons = Array.Empty<SeasonEntry>();
        view.SetState(state, ScreenPage.Characters);
        carousel = view.Root.GetComponentInChildren<ProfileCarousel>();
        Find(carousel.transform.Find("new-seasonal-profile"), "Select-seasonal").onClick.Invoke();
        Check(!Find(view.Root.transform, "CONTINUE").interactable, "Empty picker cannot continue");
        Find(view.Root.transform, "CONTINUE").onClick.Invoke();
        Check(view.DialogOpen && seasonId == "winter", "Empty picker does not submit a season");
        Find(view.Root.transform, "CANCEL").onClick.Invoke();
        view.Dispose();
        Object.DestroyImmediate(canvasObject);
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(target);
        typeof(EventSystem)
            .GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(events.GetComponent<EventSystem>(), null);
        Object.DestroyImmediate(events);

        void Capture(string name)
        {
            foreach (var label in view.Root.GetComponentsInChildren<Text>())
                label.font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
            foreach (var child in canvasObject.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 5;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            texture.Apply();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/Research/UI"));
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, name + "-" + size.x + "x" + size.y + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            RenderTexture.active = previous;
        }
    }
}
