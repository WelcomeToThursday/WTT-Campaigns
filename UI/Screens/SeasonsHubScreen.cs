using System;
using System.Collections.Generic;
using System.Linq;
using SeasonalPerks.UI.Audio;
using SeasonalPerks.UI.BattlePass;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Media;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonsHubScreen : IDisposable
{
    private readonly UiElements _ui;
    private readonly RectTransform _stage;
    private readonly Func<string, Sprite?> _art;
    private RectTransform? _page;
    private RectTransform? _content;
    private GameObject? _tooltip;
    private HubState _state = new HubState();
    private PerkEntry[] _perks = Array.Empty<PerkEntry>();
    private readonly Dictionary<int, int> _selections = new Dictionary<int, int>();
    private int _seasonalSelection;
    private bool _disposed;

    public GameObject Root { get; }
    public HubTab Tab { get; private set; }
    public int PageIndex { get; private set; }
    public int SlideIndex { get; private set; }
    public int SelectedRewardIndex
    {
        get { return _selections.TryGetValue(PageIndex, out var value) ? value : 0; }
    }
    public Action? CloseRequested;
    public Action? RetryRequested;
    public Action<InterfaceSound>? SoundRequested;
    public Action<string, Image>? ImageRequested;
    public Action<string, Image>? PerkIconRequested;
    public Action<string, RawImage, bool, Image?>? VideoRequested;
    public Action? PageClosing;

    public SeasonsHubScreen(Transform parent, Font font, Func<string, Sprite?> artwork)
    {
        _ui = new UiElements(font, sound => SoundRequested?.Invoke(sound));
        _art = artwork;
        var root = UiElements.Rect("SeasonsHubScreen", parent, 0, 0);
        UiElements.Stretch(root);
        Root = root.gameObject;
        UiElements.Fill(root, new Color(.012f, .023f, .025f, 1), true);
        _stage = UiElements.Rect("HubSafeArea", root, 1920, 1080);
        Root.SetActive(false);
    }

    public void Open()
    {
        Tab = HubTab.BattlePass;
        PageIndex = SlideIndex = _seasonalSelection = 0;
        _selections.Clear();
        Root.SetActive(true);
        Fit();
        ShowMessage("Loading season...", false);
    }

    public void SetState(HubState state, PerkEntry[] perks)
    {
        _state = state;
        _perks = perks;
        PageIndex = Math.Max(0, Math.Min(PageIndex, state.Pages.Length - 1));
        Render();
    }

    public void Fit()
    {
        var rect = ((RectTransform)Root.transform).rect;
        _stage.localScale = Vector3.one * Mathf.Min(rect.width / 1920, rect.height / 1080);
    }

    public void ShowTab(HubTab tab)
    {
        if (HasTutorial || Tab == tab)
        {
            return;
        }

        Tab = tab;
        Render();
    }

    public void ChangePage(int delta)
    {
        if (HasTutorial)
        {
            return;
        }
        if (Tab == HubTab.BattlePass)
        {
            var target = Math.Max(0, Math.Min(_state.Pages.Length - 1, PageIndex + delta));
            if (target == PageIndex)
            {
                return;
            }

            PageIndex = target;
        }
        else if (Tab == HubTab.AboutSeason)
        {
            var target = Math.Max(0, Math.Min(_state.Slides.Length - 1, SlideIndex + delta));
            if (target == SlideIndex)
            {
                return;
            }

            SlideIndex = target;
        }
        else
        {
            return;
        }

        RenderContent();
    }

    public void SelectReward(int index)
    {
        if (HasTutorial)
        {
            return;
        }
        if (Tab == HubTab.BattlePass && _state.Pages.Length > 0 && index >= 0 && index < _state.Pages[PageIndex].Rewards.Length)
        {
            if (SelectedRewardIndex == index)
            {
                return;
            }
            _selections[PageIndex] = index;
        }
        else if (Tab == HubTab.SeasonalRewards && index >= 0 && index < _state.SeasonalRewards.Length)
        {
            if (_seasonalSelection == index)
            {
                return;
            }
            _seasonalSelection = index;
        }
        else
        {
            return;
        }

        RenderContent();
    }

    public void ShowMessage(string message, bool retry)
    {
        ClearPage();
        Caption(_page!, "Status", message, 24, 360, 440, 1200, 100).alignment = TextAnchor.MiddleCenter;
        Button(_page!, "BACK", 1650, 55, 160, 42, () => CloseRequested?.Invoke());
        if (retry)
        {
            Button(_page!, "RETRY", 840, 560, 240, 44, () => RetryRequested?.Invoke());
        }
    }

    private void ClearPage()
    {
        _ready = false;
        DismissTutorial();
        DismissDialog();
        HideTooltip();
        PageClosing?.Invoke();
        if (_page)
        {
            _page!.gameObject.SetActive(false);
            UiElements.Destroy(_page.gameObject);
        }
        _content = null;
        _claimButton = null;
        _claimReward = null;
        _page = UiElements.Rect("HubPage", _stage, 1920, 1080);
        var bg = Art(_page, "HubBackground", "sharedassets48-496", 0, 0, 1920, 1080);
        bg.color = Color.white;
        Art(_page, "Vignette", "sharedassets48-380", 0, 0, 1920, 1080).type = Image.Type.Sliced;
    }

    private void Render()
    {
        if (_disposed)
        {
            return;
        }

        ClearPage();
        var root = _page!;
        TabButton(root, "BATTLE PASS", 520, 55, 300, Tab == HubTab.BattlePass, () => ShowTab(HubTab.BattlePass));
        TabButton(root, "SEASONAL REWARDS", 832, 55, 300, Tab != HubTab.BattlePass, () => ShowTab(HubTab.SeasonalRewards));
        var leagues = Caption(root, "Leagues", "LEAGUES AND RATINGS", 23, 1144, 55, 340, 40);
        leagues.color = new Color(.34f, .4f, .38f, .5f);
        Hint(leagues.gameObject, "Leagues and ratings are unavailable.", 1150, 100);
        Button(root, "BACK", 1655, 55, 155, 40, () => CloseRequested?.Invoke(), false);
        if (Tab != HubTab.BattlePass)
        {
            if (_state.LegacyBranding)
            {
                var logo = UiElements.Fill(Box(root, "SeasonLogo", 650, 110, 620, 207), Color.white);
                logo.sprite = SeasonLogoArtwork.Load();
                Video(root, "Season_1_logo_video_1380x460.webm", 650, 110, 620, 207, false, logo);
            }
            else if (_state.BannerImage.Length > 0)
            {
                Remote(root, "SeasonLogo", _state.BannerImage, 650, 110, 620, 207).preserveAspect = true;
            }
            else
            {
                Caption(root, "SeasonTitle", _state.SeasonName, 38, 650, 110, 620, 207);
            }

            Video(root, "Smoke_1144x264.webm", 445, 135, 1030, 238, true);
            TabButton(root, "SEASONAL REWARDS", 108, 316, 290, Tab == HubTab.SeasonalRewards, () => ShowTab(HubTab.SeasonalRewards));
            TabButton(root, "ABOUT THE SEASON", 414, 316, 290, Tab == HubTab.AboutSeason, () => ShowTab(HubTab.AboutSeason));
        }
        RenderContent();
    }

    private void RenderContent()
    {
        if (_disposed || !_page || !Root.activeSelf)
        {
            return;
        }

        _ready = false;
        DismissTutorial();
        DismissDialog();
        HideTooltip();
        if (_content)
        {
            _content!.gameObject.SetActive(false);
            UiElements.Destroy(_content.gameObject);
        }

        // Reward selection and paging must not recreate navigation or restart the header videos.
        _claimReward = null;
        _content = UiElements.Rect("HubContent", _page!, 1920, 1080);
        if (Tab == HubTab.BattlePass)
        {
            RenderBattlePass(_content);
        }
        else if (Tab == HubTab.SeasonalRewards)
        {
            RenderSeasonal(_content);
        }
        else
        {
            RenderAbout(_content);
        }
        if (_claimButton && _claimReward == null)
        {
            _claimButton!.transform.parent.gameObject.SetActive(false);
        }
        _ready = true;
    }

    private static RectTransform Box(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = UiElements.Rect(name, parent, width, height);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x + width / 2, -y - height / 2);
        return rect;
    }

    private Text Caption(Transform parent, string name, string value, int size, float x, float y, float width, float height)
    {
        var host = Box(parent, name, x, y, width, height);
        var label = _ui.Label(host, "Text", value, size, width, height);
        label.color = new Color(.62f, .65f, .61f);
        return label;
    }

    private Image Art(Transform parent, string name, string asset, float x, float y, float width, float height)
    {
        var image = UiElements.Fill(Box(parent, name, x, y, width, height), Color.white);
        image.sprite = _art(asset);
        if (!image.sprite)
        {
            image.color = Color.clear;
        }

        return image;
    }

    private Image Remote(Transform parent, string name, string asset, float x, float y, float width, float height)
    {
        var image = UiElements.Fill(Box(parent, name, x, y, width, height), Color.clear);
        image.preserveAspect = true;
        ImageRequested?.Invoke(asset, image);
        return image;
    }

    private Button Button(Transform parent, string text, float x, float y, float width, float height, Action action, bool fill = true)
    {
        var host = Box(parent, text, x, y, width, height);
        var button = _ui.Button(host, text, width, 0, 0, action, height);
        if (!fill)
        {
            ((Image)button.targetGraphic).color = Color.clear;
        }

        return button;
    }

    private void TabButton(Transform parent, string text, float x, float y, float width, bool selected, Action action)
    {
        var b = Button(parent, text, x, y, width, 34, action);
        var bg = (Image)b.targetGraphic;
        bg.sprite = _art("resources-6036");
        bg.type = Image.Type.Sliced;
        bg.color = selected ? Color.white : Color.clear;
        b.GetComponentInChildren<Text>().color = selected ? new Color(.13f, .2f, .16f) : UiElements.Ink;
        b.GetComponentInChildren<Text>().fontSize = 23;
    }

    private void DisabledAction(Transform parent, string label, string asset, float x, float y, float width)
    {
        var image = Art(parent, label, asset, x, y, width, 36);
        image.type = Image.Type.Sliced;
        var text = Caption(image.transform, "Caption", label, 16, 0, 0, width, 36);
        text.alignment = TextAnchor.MiddleCenter;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = false;
        button.transition = Selectable.Transition.None;
        Hint(image.gameObject, "Online document purchases are unavailable in SPT.", x, y - 85);
    }

    private void Hint(GameObject target, string text, float x, float y)
    {
        var image = target.GetComponent<Graphic>();
        if (image)
        {
            image.raycastTarget = true;
        }

        var pointer = target.GetComponent<HubPointer>() ?? target.AddComponent<HubPointer>();
        pointer.Hover = over =>
        {
            if (over)
            {
                ShowTooltip(text, x, y);
            }
            else
            {
                HideTooltip();
            }
        };
    }

    private void ShowTooltip(string value, float x, float y)
    {
        if (HasTutorial)
        {
            return;
        }
        HideTooltip();
        var height = Mathf.Clamp(60 + value.Length / 45 * 21, 80, 300);
        var rect = Box(_stage, "HubTooltip", Mathf.Clamp(x, 20, 1450), Mathf.Clamp(y, 20, 1060 - height), 450, height);
        _tooltip = rect.gameObject;
        UiElements.Fill(rect, new Color(.01f, .025f, .018f, .98f));
        var label = Caption(rect, "TooltipText", value, 18, 14, 8, 422, height - 16);
        label.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private void HideTooltip()
    {
        if (_tooltip)
        {
            UiElements.Destroy(_tooltip!);
        }

        _tooltip = null;
    }

    private void Video(Transform parent, string name, float x, float y, float width, float height, bool loop, Image? fallback = null)
    {
        if (VideoRequested == null)
        {
            return;
        }

        var image = Box(parent, name, x, y, width, height).gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.clear;
        VideoRequested(name, image, loop, fallback);
    }

    public void Close()
    {
        _ready = false;
        DismissTutorial();
        DismissDialog();
        HideTooltip();
        PageClosing?.Invoke();
        Root.SetActive(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();
        UiElements.Destroy(Root);
    }
}
