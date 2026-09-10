using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.BattlePass;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;

namespace WTT.Campaigns.UI.Screens;

public sealed partial class CampaignScreen
{
    // Live level48 SeasonsIntroScreen 8659 uses SeasonCarouselData 2693.
    private static readonly string[] IntroductionArtwork =
    {
        "sharedassets48-389",
        "sharedassets48-480",
        "sharedassets48-324",
        "sharedassets48-500",
        "sharedassets48-493",
    };

    private static readonly string[] IntroductionText =
    {
        "<color=#83C5A9>WELCOME TO SEASONS</color>",
        "<color=#83C5A9>SEASONS</color>\n\nExplore seasonal content, modifiers, a Battle Pass\nand seasonal rewards. Choose from the seasons installed\non your SPT server.",
        "<color=#83C5A9>SEASONAL CHARACTER</color>\n\nCreate a separate seasonal PMC with its own equipment\nand progression. Your regular character remains available.\nCreate several characters and choose a season for each.",
        "<color=#83C5A9>MODIFIER SYSTEM</color>\n\nCustomize your character with personal modifiers.\nBalance beneficial perks with detrimental modifiers.\nCommon modifiers apply to the seasonal PMC.",
        "<color=#83C5A9>BATTLE PASS</color>\n\nCollect documents in raids and complete requirements\nto unlock rewards in the Seasons hub.\nProgress belongs to your local seasonal character.",
    };

    private RectTransform? _introduction;
    private Image? _introductionImage;
    private Text? _introductionText;
    private Image? _introductionOutgoingImage;
    private Text? _introductionOutgoingText;
    private Text? _introductionCounter;
    private CanvasGroup? _introductionUnderlying;
    private bool _introductionInteractable;
    private bool _introductionRaycasts;

    public bool SeasonIntroductionOpen => _introduction != null;
    public int SeasonIntroductionPage { get; private set; }

    public void ShowSeasonIntroduction()
    {
        if (_disposed || _embedded || _busy || DialogOpen || !Root.activeInHierarchy || Page != ScreenPage.Characters)
        {
            return;
        }
        ClearCardHover();
        _introductionUnderlying = _body.GetComponent<CanvasGroup>();
        if (!_introductionUnderlying)
        {
            _introductionUnderlying = _body.gameObject.AddComponent<CanvasGroup>();
        }
        _introductionInteractable = _introductionUnderlying.interactable;
        _introductionRaycasts = _introductionUnderlying.blocksRaycasts;
        _introductionUnderlying.interactable = _introductionUnderlying.blocksRaycasts = false;
        if (EventSystem.current)
        {
            _previousFocus = EventSystem.current.currentSelectedGameObject;
            EventSystem.current.SetSelectedGameObject(null);
        }
        _introduction = UiElements.Rect("SeasonsIntroduction", _panel, 1920, 1080);
        _dialog = _introduction.gameObject;
        UiElements.Fill(_introduction, new Color(.008f, .012f, .01f, .98f), true);
        var content = UiElements.Rect("SeasonInfoCarousel", _introduction, 1050, 677);
        UiElements.Fill(content, new Color(.025f, .035f, .03f), true);
        content.gameObject.AddComponent<HubPointer>().Scroll = ChangeSeasonIntroductionPage;
        _introductionOutgoingImage = UiElements.Fill(UiElements.Rect("OutgoingBackground", content, 1050, 621, 0, 28), Color.white);
        _introductionOutgoingText = _ui.Label(content, "OutgoingBody", "", 18, 840, 106, 0, -209.5f);
        _introductionOutgoingText.supportRichText = true;
        _introductionOutgoingText.alignment = TextAnchor.MiddleCenter;
        _introductionOutgoingText.color = new Color(.514f, .773f, .663f);
        _introductionOutgoingImage.canvasRenderer.SetAlpha(0);
        _introductionOutgoingText.canvasRenderer.SetAlpha(0);
        _introductionImage = UiElements.Fill(UiElements.Rect("Background", content, 1050, 621, 0, 28), Color.white);
        _introductionText = _ui.Label(content, "Body", "", 18, 840, 106, 0, -209.5f);
        _introductionText.supportRichText = true;
        _introductionText.alignment = TextAnchor.MiddleCenter;
        _introductionText.color = new Color(.514f, .773f, .663f);
        var navigation = UiElements.Rect("Navigation", content, 1050, 56, 0, -310.5f);
        UiElements.Fill(navigation, new Color(.235f, .294f, .267f, .1f));
        UiElements.Fill(UiElements.Rect("Divider", navigation, 1050, 1, 0, 28), new Color(.25f, .34f, .29f));
        var previous = _ui.Button(navigation, "<", 40, -62.5f, 0, () => ChangeSeasonIntroductionPage(-1), 40);
        previous.name = "IntroductionPrevious";
        var next = _ui.Button(navigation, ">", 40, 62.5f, 0, () => ChangeSeasonIntroductionPage(1), 40);
        next.name = "IntroductionNext";
        previous.navigation = next.navigation = new Navigation { mode = Navigation.Mode.None };
        _introductionCounter = _ui.Label(navigation, "PageCounter", "", 16, 85, 24);
        _introductionCounter.alignment = TextAnchor.MiddleCenter;
        _ui.Label(navigation, "PreviousShortcut", "Q", 16, 30, 24, -110).alignment = TextAnchor.MiddleCenter;
        _ui.Label(navigation, "NextShortcut", "E", 16, 30, 24, 110).alignment = TextAnchor.MiddleCenter;
        var close = _ui.Button(_introduction, "CLOSE", 140, -100, -40, DismissDialog, 44, InterfaceSound.Back);
        close.name = "IntroductionClose";
        close.targetGraphic.color = Color.clear;
        close.GetComponentInChildren<Text>().fontSize = 24;
        var closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = Vector2.one;
        close.navigation = new Navigation { mode = Navigation.Mode.None };
        SeasonIntroductionPage = 0;
        RenderSeasonIntroductionPage(false);
        Fit();
    }

    public void ChangeSeasonIntroductionPage(int direction)
    {
        if (_busy || !SeasonIntroductionOpen || !Root.activeInHierarchy || direction == 0)
        {
            return;
        }
        // The recovered CarouselView loops in both directions.
        SeasonIntroductionPage =
            (SeasonIntroductionPage + (direction > 0 ? 1 : -1) + IntroductionArtwork.Length) % IntroductionArtwork.Length;
        RenderSeasonIntroductionPage(true);
    }

    private void RenderSeasonIntroductionPage(bool animate)
    {
        _introductionOutgoingImage!.sprite = _introductionImage!.sprite;
        _introductionOutgoingText!.text = _introductionText!.text;
        ArtworkRequested?.Invoke("hub:" + IntroductionArtwork[SeasonIntroductionPage], _introductionImage!);
        _introductionText!.text = IntroductionText[SeasonIntroductionPage];
        _introductionCounter!.text = (SeasonIntroductionPage + 1) + " / " + IntroductionArtwork.Length;
        // Live CarouselView crossfades pages over 0.15 seconds, including while game time is paused.
        foreach (var graphic in new Graphic[] { _introductionImage!, _introductionText! })
        {
            graphic.CrossFadeAlpha(animate && Application.isPlaying ? 0 : 1, 0, true);
            graphic.CrossFadeAlpha(1, .15f, true);
        }
        foreach (var graphic in new Graphic[] { _introductionOutgoingImage!, _introductionOutgoingText! })
        {
            graphic.CrossFadeAlpha(animate && Application.isPlaying ? 1 : 0, 0, true);
            graphic.CrossFadeAlpha(0, .15f, true);
        }
    }

    private void FitSeasonIntroduction(Vector2 viewport)
    {
        if (_introduction)
        {
            _introduction!.sizeDelta = viewport;
        }
    }

    private void CloseSeasonIntroduction()
    {
        if (_introductionUnderlying)
        {
            _introductionUnderlying!.interactable = _introductionInteractable;
            _introductionUnderlying.blocksRaycasts = _introductionRaycasts;
        }
        _introductionUnderlying = null;
        _introduction = null;
        _introductionImage = null;
        _introductionText = null;
        _introductionOutgoingImage = null;
        _introductionOutgoingText = null;
        _introductionCounter = null;
    }
}
