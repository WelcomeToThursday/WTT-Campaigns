using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;

namespace WTT.Campaigns.UI.Screens;

public sealed partial class CampaignScreen
{
    private Image CreationArt(string name, float width, float height, float x, float y)
    {
        var image = UiElements.Fill(UiElements.Rect(name, _body, width, height, x, y), Color.white);
        image.enabled = false;
        ArtworkRequested?.Invoke(name, image);
        return image;
    }

    private void BuildCreation()
    {
        _creationBackground = CreationArt("background", 1920, 1080, 0, 0).rectTransform;
        if (Page == ScreenPage.CreationIdentity)
        {
            var host = UiElements.Rect("NativeIdentity", _body, 1920, 1080);
            if (IdentityRequested != null)
            {
                _identity = IdentityRequested(
                    host,
                    _creationDraft,
                    () =>
                    {
                        _name = _creationDraft.Nickname;
                        _side = _creationDraft.Side;
                        ShowPage(ScreenPage.CreationCommon);
                    },
                    () => WithDiscardConfirmation(() => ShowPage(ScreenPage.Characters))
                );
            }
            else
            {
                _ui.Label(host, "NativeIdentityNotice", "Faction and appearance are provided by EFT in game.", 24, 1200, 60).alignment =
                    TextAnchor.MiddleCenter;
                CreationButton("BACK", -480, RequestClose);
            }
        }
        else
        {
            var common = Page == ScreenPage.CreationCommon;
            _ui.Label(_body, "CreationTitle", common ? "COMMON MODIFIERS" : "PERSONAL MODIFIERS", 42, 1600, 60, 0, 468).alignment =
                TextAnchor.MiddleCenter;
            var explanation = common
                ? "Rules configured for your seasonal character. These cannot be selected or changed here."
                : "Positive and negative traits that apply only to your character. Each modifier has a point cost.\n"
                    + (
                        _state.EnforceBudget
                            ? "To create a character, the total points must be 0 or higher."
                            : "Point enforcement is disabled on this server."
                    );
            _ui.Label(_body, "CreationExplanation", explanation, 20, 1660, 66, 0, 397).alignment = TextAnchor.MiddleCenter;
            if (common)
            {
                var season = _state.Seasons.FirstOrDefault(value => value.Id == _state.SeasonId);
                var label = _ui.Label(
                    _body,
                    "ChosenSeason",
                    season?.Name.ToUpperInvariant() ?? "SEASONAL CHARACTER",
                    32,
                    1400,
                    100,
                    0,
                    292
                );
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(.392f, .855f, .655f);
            }
            else
            {
                _ui.Label(_body, "NegativeHeading", "Negative (+)", 26, 620, 36, -530, 302);
                _ui.Label(_body, "NegativeHint", "Grant points", 17, 620, 30, -530, 270).color = UiElements.Muted;
                _ui.Label(_body, "PositiveHeading", "Positive (-)", 26, 620, 36, 530, 302).alignment = TextAnchor.MiddleRight;
                _ui.Label(_body, "PositiveHint", "Cost points", 17, 620, 30, 530, 270).color = UiElements.Muted;
                _body.Find("PositiveHint").GetComponent<Text>().alignment = TextAnchor.MiddleRight;
                _creationPoints = _ui.Label(_body, "CreationPoints", "", 25, 400, 44, 0, 302);
                _creationPoints.alignment = TextAnchor.MiddleCenter;
                _creationReset = _ui.Button(_body, "RESET", 150, -788, -455, ResetSelection, clickSound: InterfaceSound.PerkReset);
                _creationReset.targetGraphic.color = Color.clear;
            }
            var left = _ui.Scroll(_body, "CreationNegative", 850, common ? 585 : 622, -440, common ? -84 : -74);
            var right = _ui.Scroll(_body, "CreationPositive", 850, common ? 585 : 622, 440, common ? -84 : -74);
            foreach (var scroll in new[] { left, right })
            {
                scroll.GetComponent<Image>().color = Color.clear;
                scroll.content.GetComponent<VerticalLayoutGroup>().spacing = 14;
                if (common)
                {
                    scroll.verticalScrollbar.gameObject.SetActive(false);
                }
            }
            var entries = _state.Perks.Where(perk => perk.Common == common).ToArray();
            for (var i = 0; i < entries.Length; i++)
            {
                var perk = entries[i];
                // Preserve catalogue order, including the live common two-column ordering.
                var column =
                    common ? (i < (entries.Length + 1) / 2 ? left : right)
                    : perk.Points > 0 ? left
                    : right;
                var card = BuildCard(perk, column.content);
                StyleCreationCard(card, perk);
                _cards.Add((perk, card));
            }
            _creationNext = CreationButton(
                "NEXT",
                -440,
                () =>
                {
                    if (_busy || _dialog != null)
                    {
                        return;
                    }
                    if (Page == ScreenPage.CreationCommon)
                    {
                        ShowPage(ScreenPage.CreationPersonal);
                    }
                    else if (ValidSelection())
                    {
                        ReviewCreation();
                    }
                }
            );
            _creationBack = CreationButton("BACK", -490, RequestClose);
            RefreshCards();
        }
        var glow = CreationArt("top-glow-seasonal", 1920, 512, 0, 540);
        glow.rectTransform.pivot = new Vector2(.5f, 1);
        glow.color = new Color(1, 1, 1, .6f);
        glow.material = GlowMaterial;
        _creationGlow = glow.rectTransform;
    }

    private Button CreationButton(string caption, float y, Action action)
    {
        var button = _ui.Button(_body, caption, 240, 0, y, action, 48);
        button.targetGraphic.color = Color.clear;
        button.GetComponentInChildren<Text>().fontSize = 28;
        return button;
    }

    private void CreateCharacter()
    {
        if (_busy || _dialog != null || Created || !ValidSelection() || SaveRequested == null)
        {
            return;
        }
        // ACCEPT in Save modifiers submits the draft. Lock the view before dispatch,
        // so a second click cannot start another creation while the request is pending.
        SetBusy(true);
        try
        {
            SaveRequested();
        }
        catch
        {
            SetBusy(false);
            throw;
        }
    }

    private void StyleCreationCard(GameObject card, PerkEntry perk)
    {
        var height = perk.Common ? 174 : 140;
        ((RectTransform)card.transform).sizeDelta = new Vector2(805, height);
        var layout = card.GetComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = height;
        var content = card.transform.Find("Content");
        content.Find("Stripe").gameObject.SetActive(false);
        content.Find("SignedPoints").gameObject.SetActive(false);
        var name = content.Find("Info/NameContainer/Name").GetComponent<Text>();
        name.text = perk.Name.ToUpperInvariant() + (perk.Common ? "" : " (" + (perk.Points > 0 ? "+" : "") + perk.Points + ")");
        name.fontSize = 23;
        var description = content.Find("Info/Description").GetComponent<Text>();
        description.text = perk.Description;
        description.fontSize = 19;
        Place((RectTransform)content.Find("SelectionMark"), 38, 34, -26, height / 2f - 25, new Vector2(1, .5f));
        content.Find("SelectionMark").gameObject.SetActive(!perk.Common);
        var status = content.Find("SelectionState").GetComponent<Text>();
        status.rectTransform.anchoredPosition = new Vector2(134, -height / 2f + 13);
        status.fontSize = 12;
        // Long captured descriptions must remain readable rather than being clipped.
        var required = description.preferredHeight + 76;
        layout.minHeight = layout.preferredHeight = Mathf.Max(height, required);
    }

    private void RefreshCreation()
    {
        if (_creationBack)
        {
            _creationBack!.interactable = !_busy;
            _creationBack.GetComponentInChildren<Text>().color = _busy ? UiElements.Muted : UiElements.Ink;
        }
        if (_creationNext)
        {
            _creationNext!.interactable = !_busy && (Page == ScreenPage.CreationCommon || ValidSelection());
            _creationNext.GetComponentInChildren<Text>().color = _creationNext.interactable ? UiElements.Ink : UiElements.Muted;
        }
        if (_creationPoints)
        {
            _creationPoints!.text = "POINTS LEFT: " + Remaining;
            _creationPoints.color = _state.EnforceBudget && Remaining < 0 ? UiElements.Negative : UiElements.Ink;
        }
        if (_creationReset)
        {
            _creationReset!.interactable = !_busy && _selected.Count > 0;
        }
    }

    private void CreationBack()
    {
        if (Page == ScreenPage.CreationPersonal)
        {
            ShowPage(ScreenPage.CreationCommon);
        }
        else if (Page == ScreenPage.CreationCommon)
        {
            _creationDraft.Appearance = true;
            ShowPage(ScreenPage.CreationIdentity);
        }
        else if (_identity != null)
        {
            _identity.Back();
        }
        else
        {
            WithDiscardConfirmation(() => ShowPage(ScreenPage.Characters));
        }
    }
}
