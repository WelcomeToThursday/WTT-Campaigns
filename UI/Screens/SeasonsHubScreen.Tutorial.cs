using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

public sealed partial class SeasonsHubScreen
{
    private GameObject? _tutorial;
    private int _tutorialStep = -1;
    private bool _ready;
    private static readonly Color TutorialInk = new Color(.514f, .773f, .663f);

    public Action? TutorialCompleted;
    public bool HasTutorial
    {
        get { return _tutorialStep >= 0; }
    }
    public int TutorialStep
    {
        get { return _tutorialStep; }
    }

    private void TutorialButton(Transform root)
    {
        var button = Button(root, "BattlePassTutorial", 266, 67, 32, 32, StartTutorial, false);
        button.GetComponentInChildren<Text>().text = "";
        Art(button.transform, "InfoIcon", "sharedassets48-454", 4, 4, 24, 24);
        Hint(button.gameObject, "Press to start the tutorial", 265, 110);
    }

    public void StartTutorial()
    {
        if (_disposed || !_ready || !Root.activeSelf || Tab != HubTab.BattlePass || HasDialog || HasTutorial)
        {
            return;
        }
        HideTooltip();
        SetTutorialInput(true);
        _tutorialStep = 0;
        RenderTutorial();
    }

    public void AdvanceTutorial()
    {
        if (!HasTutorial)
        {
            return;
        }
        if (_tutorialStep == 7)
        {
            FinishTutorial();
            return;
        }
        _tutorialStep++;
        RenderTutorial();
    }

    public void PreviousTutorialStep()
    {
        if (_tutorialStep <= 0)
        {
            return;
        }
        _tutorialStep--;
        RenderTutorial();
    }

    public void DismissTutorial()
    {
        RemoveTutorialOverlay();
        _tutorialStep = -1;
        SetTutorialInput(false);
    }

    private void FinishTutorial()
    {
        if (!HasTutorial)
        {
            return;
        }
        DismissTutorial();
        TutorialCompleted?.Invoke();
    }

    private void SetTutorialInput(bool blocked)
    {
        if (_page)
        {
            var group = _page!.GetComponent<CanvasGroup>();
            if (blocked && !group)
            {
                group = _page.gameObject.AddComponent<CanvasGroup>();
            }
            if (group)
            {
                group.interactable = group.blocksRaycasts = !blocked;
            }
        }
        if (EventSystem.current)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void RemoveTutorialOverlay()
    {
        if (_tutorial)
        {
            // Destroy is deferred in game; stop this step receiving another click immediately.
            _tutorial!.SetActive(false);
            UiElements.Destroy(_tutorial);
        }
        _tutorial = null;
    }

    private void RenderTutorial()
    {
        RemoveTutorialOverlay();
        if (EventSystem.current)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
        var overlay = Box(_stage, "BattlePassTutorialOverlay", 0, 0, 1920, 1080);
        _tutorial = overlay.gameObject;
        UiElements.Fill(overlay, Color.clear, true);
        if (_tutorialStep == 7)
        {
            UiElements.Fill(overlay, new Color(0, 0, 0, .86f), true);
            TutorialBriefing(overlay);
            return;
        }

        // The recovered live tutorial targets the same seven regions in this order.
        var areas = new[]
        {
            new Rect(100, 142, 432, 890),
            new Rect(1388, 142, 432, 786),
            new Rect(546, 922, 666, 132),
            new Rect(546, 960, 90, 94),
            new Rect(1206, 960, 170, 94),
            new Rect(1068, 924, 312, 42),
            new Rect(1388, 1001, 432, 52),
        };
        var area = areas[_tutorialStep];
        TutorialShade(overlay, "Above", 0, 0, 1920, area.y);
        TutorialShade(overlay, "Below", 0, area.yMax, 1920, 1080 - area.yMax);
        TutorialShade(overlay, "Left", 0, area.y, area.x, area.height);
        TutorialShade(overlay, "Right", area.xMax, area.y, 1920 - area.xMax, area.height);
        var focus = Button(overlay, "TutorialFocus", area.x, area.y, area.width, area.height, AdvanceTutorial, false);
        focus.GetComponentInChildren<Text>().text = "";
        focus.transition = Selectable.Transition.None;
        ExchangeFrame(overlay, area.x, area.y, area.width, area.height, TutorialInk);

        var position =
            _tutorialStep == 0 ? new Vector2(590, 350)
            : _tutorialStep == 1 ? new Vector2(800, 390)
            : _tutorialStep == 3 ? new Vector2(970, 640)
            : _tutorialStep == 4 ? new Vector2(710, 640)
            : _tutorialStep == 6 ? new Vector2(840, 660)
            : new Vector2(650, 630);
        var hint = Box(overlay, "TutorialHint", position.x, position.y, 540, 268);
        UiElements.Fill(hint, new Color(.018f, .04f, .033f, .98f), true);
        ExchangeFrame(hint, 0, 0, 540, 268, TutorialInk * .65f);
        Caption(hint, "StepNumber", "0" + (_tutorialStep + 1) + " / 08", 22, 22, 12, 180, 34).color = TutorialInk;
        var text = Caption(hint, "TutorialText", TutorialText(), 21, 22, 55, 496, 145);
        text.supportRichText = true;
        text.alignment = TextAnchor.UpperLeft;
        var previous = Button(hint, "PREVIOUS", 20, 216, 140, 34, PreviousTutorialStep, false);
        previous.interactable = _tutorialStep > 0;
        Button(hint, "NEXT", 370, 216, 148, 34, AdvanceTutorial);
        Button(overlay, "SKIP TUTORIAL", 1570, 62, 240, 38, FinishTutorial, false);
        Caption(overlay, "TutorialKeys", "Q / E   Previous / Next     •     ESC   Close tutorial", 16, 620, 1048, 680, 26).alignment =
            TextAnchor.MiddleCenter;

        if (_tutorialStep == 3 && _state.Documents.Length > 0)
        {
            var document = _state.Documents[0];
            var card = Box(overlay, "TutorialDocumentInfo", 546, 655, 390, 280);
            UiElements.Fill(card, new Color(.02f, .04f, .033f, 1), true);
            ExchangeFrame(card, 0, 0, 390, 280, TutorialInk);
            Remote(card, "DocumentImage", document.Image, 16, 16, 72, 72);
            Caption(card, "DocumentName", document.Name, 22, 104, 16, 266, 72).color = TutorialInk;
            Caption(card, "DocumentDetails", DocumentHelp(document.Count), 19, 18, 106, 354, 158).alignment = TextAnchor.UpperLeft;
        }
    }

    private void TutorialShade(Transform parent, string name, float x, float y, float width, float height)
    {
        UiElements.Fill(Box(parent, "TutorialShade" + name, x, y, width, height), new Color(0, 0, 0, .82f), true);
    }

    private string TutorialText()
    {
        switch (_tutorialStep)
        {
            case 0:
                return "Here you'll find <color=#83C5A9>Battle Pass rewards</color>.\nSelect an item to view its unlock requirements. Browse reward pages with Q / E or the arrows.";
            case 1:
                return "To unlock a reward, <color=#83C5A9>meet all of its requirements</color>.\nCollect the required documents, then select CLAIM REWARD and confirm the handover.";
            case 2:
                return "<color=#83C5A9>Documents</color> are used to unlock Battle Pass rewards.\nFind them in Seasonal PMC raids and extract with them. Your owned documents appear here.";
            case 3:
                return "Hover over a document to see <color=#83C5A9>its name, your owned count and where to look</color>.\nDocuments can be found in eligible jackets, filing drawers, safes and duffel bags.";
            case 4:
                return "<color=#83C5A9>Classified documents</color> can replace any ordinary document when claiming a reward.\nEach covers one missing document, after confirmation. They cannot be exchanged.";
            case 5:
                return "You can collect up to <color=#83C5A9>"
                    + _state.DocumentLimit
                    + " documents per "
                    + TimeSpan.FromSeconds(_state.WindowSeconds).TotalHours.ToString("0.##")
                    + "-hour window</color>.\nThe window starts with your first pickup. Hover over the limit to see when it resets.";
            default:
                return _state.ExchangeUnavailableReason.Length > 0
                    ? "<color=#83C5A9>Exchange documents</color> lets you trade ordinary documents.\nExchanges are currently unavailable for this season."
                    : "<color=#83C5A9>Exchange documents</color> for another type or an available crate.\nChoose a category, select the documents to spend, then choose an output. Review the displayed cost before confirming.";
        }
    }

    private static string DocumentHelp(int count)
    {
        return "Owned: "
            + count
            + "\n\nLook in jackets, filing drawers, safes and duffel bags during Seasonal PMC raids. Extract with documents to use them for rewards.";
    }

    private void TutorialBriefing(Transform overlay)
    {
        var panel = Box(overlay, "TutorialBriefing", 320, 120, 1280, 781);
        UiElements.Fill(panel, new Color(.016f, .035f, .029f, 1), true);
        Art(panel, "BriefingBackground", "sharedassets48-400", 0, 0, 1280, 781);
        ExchangeFrame(panel, 0, 0, 1280, 781, TutorialInk);
        Caption(panel, "BriefingLabel", "BATTLEPASS_SYSTEM_v1.     // FINAL BRIEFING", 14, 56, 15, 1168, 35).color = TutorialInk;
        Caption(panel, "BriefingTitle", "BATTLE PASS", 52, 56, 95, 1168, 70).color = TutorialInk;
        Caption(panel, "BriefingSubtitle", "SYSTEM INFORMATION", 22, 56, 174, 1168, 40).color = TutorialInk;
        var titles = new[] { "SEASONAL PROGRESSION", "REWARDS", "DOCUMENTS & EXCHANGES" };
        var bodies = new[]
        {
            "Battle Pass progress belongs to your active Seasonal character and season.\n\nNormal characters do not use this Battle Pass. Progress is saved locally for this character and season.",
            "Claim rewards by meeting their requirements and submitting documents.\n\nItems go to your stash. Other rewards unlock their supported local features. Unavailable rewards explain what is missing.",
            "Collect documents in Seasonal PMC raids. Classified documents cover claim shortages one for one.\n\nExchanges use ordinary documents. Crates are offered when their contents are available. Online purchases are unavailable.",
        };
        for (var i = 0; i < titles.Length; i++)
        {
            var x = 56 + i * 396;
            Caption(panel, "BriefingHeading" + i, titles[i], 22, x, 265, 370, 60).color = TutorialInk;
            Caption(panel, "BriefingBody" + i, bodies[i], 21, x, 340, 356, 285).alignment = TextAnchor.UpperLeft;
        }
        Caption(panel, "CompleteLabel", "08 / 08     TUTORIAL COMPLETE", 16, 56, 700, 430, 38).color = TutorialInk;
        Button(panel, "PREVIOUS", 660, 700, 200, 40, PreviousTutorialStep, false);
        Button(panel, "FINISH", 924, 700, 300, 40, FinishTutorial);
        Caption(overlay, "FinishKeys", "Press any key to finish     •     ESC   Close tutorial", 18, 600, 938, 720, 40).alignment =
            TextAnchor.MiddleCenter;
    }
}
