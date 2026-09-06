using System;
using System.Linq;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonalScreen
{
    private GameObject? _previousFocus;

    private void ShowTooltip(PerkEntry perk, RectTransform card)
    {
        if (_busy || _dialog != null || !Root.activeInHierarchy)
        {
            return;
        }
        HideTooltip();
        var local = _panel.InverseTransformPoint(card.position);
        var tooltip = UiElements.Rect(
            "PerkTooltip",
            _panel,
            760,
            240,
            local.x < 0 ? 460 : -460,
            Mathf.Clamp(local.y, -205, 205)
        );
        _tooltip = tooltip.gameObject;
        UiElements.Fill(tooltip, new Color(.014f, .016f, .012f, .99f));
        UiElements.Fill(UiElements.Rect("TopLine", tooltip, 760, 2, 0, 119), UiElements.Accent);
        _ui.Label(tooltip, "PerkName", perk.Name, 23, 720, 44, 0, 84);
        var reason = PersonalPage ? LockReason(perk) : perk.Unavailable;
        var text = _ui.Label(
            tooltip,
            "Details",
            perk.Description + (reason.Length == 0 ? "" : "\n\n" + reason),
            19,
            720,
            155,
            0,
            -25
        );
        text.alignment = TextAnchor.UpperLeft;
        var height = Mathf.Clamp(text.preferredHeight, 130, 310);
        tooltip.sizeDelta = new Vector2(760, height + 100);
        text.rectTransform.sizeDelta = new Vector2(720, height);
        Place((RectTransform)tooltip.Find("TopLine"), 760, 2, 0, (height + 100) / 2 - 1);
        Place((RectTransform)tooltip.Find("PerkName"), 720, 44, 0, (height + 100) / 2 - 30);
        text.rectTransform.anchoredPosition = new Vector2(0, -28);
    }

    public void HideTooltip()
    {
        if (_tooltip)
        {
            _tooltip!.SetActive(false);
            UiElements.Destroy(_tooltip);
        }
        _tooltip = null;
    }

    private RectTransform Dialog(string title, string description, string accept, Action action)
    {
        DismissDialog();
        HideTooltip();
        _dialog = Object.Instantiate(_prefab("level49-2761"), _panel, false);
        _dialog.name = "SeasonalConfirmation";
        ClearCardHover();
        _dialog.SetActive(true);
        DisableLayout(_dialog);
        UiElements.Stretch((RectTransform)_dialog.transform, -100, -100, -100, -100);
        UiElements.Fill((RectTransform)_dialog.transform, new Color(0, 0, 0, .82f), true);
        foreach (Transform child in _dialog.transform)
        {
            child.gameObject.SetActive(child.name == "Window");
        }
        var window = (RectTransform)_dialog.transform.Find("Window");
        Place(window, 1100, 700, 0, 0);
        UiElements.Fill(window, new Color(.045f, .05f, .040f), true);
        foreach (Transform child in window)
        {
            child.gameObject.SetActive(false);
        }
        _ui.Label(window, "Title", title, 28, 1000, 52, 0, 295);
        _ui.Label(window, "Description", description, 19, 1000, 66, 0, 231).color =
            UiElements.Muted;
        UiElements.Fill(
            UiElements.Rect("Separator", window, 1000, 1, 0, 183),
            new Color(.28f, .28f, .22f)
        );
        var cancel = _ui.Button(window, "CANCEL", 230, 125, -290, DismissDialog);
        var accepted = false;
        var proceed = _ui.Button(
            window,
            accept,
            230,
            385,
            -290,
            () =>
            {
                if (_busy || accepted)
                {
                    return;
                }
                accepted = true;
                DismissDialog();
                action();
            }
        );
        cancel.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = proceed,
            selectOnRight = proceed,
            selectOnUp = proceed,
            selectOnDown = proceed,
        };
        proceed.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = cancel,
            selectOnRight = cancel,
            selectOnUp = cancel,
            selectOnDown = cancel,
        };
        if (EventSystem.current)
        {
            _previousFocus = EventSystem.current.currentSelectedGameObject;
            EventSystem.current.SetSelectedGameObject(cancel.gameObject);
        }
        return window;
    }

    private void Confirm(string title, string description, string accept, Action action)
    {
        var window = Dialog(title, description, accept, action);
        _ui.Label(
            window,
            "CurrentDraft",
            $"{_selected.Count} personal perks selected\n{Remaining} points remaining",
            26,
            960,
            140,
            0,
            10
        );
    }

    public void ReviewSelection()
    {
        if (_busy || _dialog != null || !ValidSelection())
        {
            return;
        }
        var window = Dialog(
            Created ? "CONFIRM PERK CHANGES" : "CREATE SEASONAL CHARACTER",
            Created
                ? "These selections replace your seasonal perk choices when you save."
                : (string.IsNullOrWhiteSpace(_name) ? "Seasonal" : _name)
                    + "  /  "
                    + _side.ToUpperInvariant()
                    + "  /  Separate seasonal PMC",
            Created ? "SAVE CHANGES" : "CREATE CHARACTER",
            () => SaveRequested?.Invoke()
        );
        _ui.Label(
            window,
            "Budget",
            Remaining + " POINTS REMAINING",
            18,
            480,
            44,
            -260,
            -290
        ).color = UiElements.Positive;
        var scroll = _ui.Scroll(window, "ReviewList", 1000, 400, 0, -30);
        foreach (var beneficial in new[] { false, true })
        {
            var perks = _state
                .Perks.Where(perk =>
                    !perk.Common && _selected.Contains(perk.Id) && (perk.Points < 0) == beneficial
                )
                .OrderBy(perk => perk.Name)
                .ToArray();
            if (perks.Length == 0)
            {
                continue;
            }
            var heading = _ui.Label(
                scroll.content,
                "Group",
                beneficial ? "BENEFICIAL" : "DETRIMENTAL",
                18,
                940,
                34
            );
            heading.color = beneficial ? UiElements.Positive : UiElements.Negative;
            heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            foreach (var perk in perks)
            {
                var row = UiElements.Rect(perk.Id, scroll.content, 950, 80);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
                UiElements.Fill(row, new Color(.067f, .073f, .058f));
                var icon = UiElements.Fill(
                    UiElements.Rect("Icon", row, 62, 62, -435, 0),
                    Color.clear
                );
                icon.preserveAspect = true;
                IconRequested?.Invoke(perk.Id, icon);
                _ui.Label(
                    row,
                    "Perk",
                    perk.Name + "  (" + (perk.Points > 0 ? "+" : "") + perk.Points + ")",
                    20,
                    810,
                    32,
                    40,
                    18
                );
                _ui.Label(row, "Description", perk.Description, 15, 810, 38, 40, -18).color =
                    UiElements.Muted;
            }
        }
        if (_selected.Count == 0)
        {
            var label = _ui.Label(
                scroll.content,
                "Empty",
                "No personal perks selected.\nThe configured global rules will still apply.",
                23,
                950,
                140
            );
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 140;
        }
    }

    public void DismissDialog()
    {
        if (_dialog)
        {
            _dialog!.SetActive(false);
            UiElements.Destroy(_dialog);
        }
        _dialog = null;
        if (EventSystem.current && _previousFocus && _previousFocus!.activeInHierarchy)
        {
            EventSystem.current.SetSelectedGameObject(_previousFocus);
        }
        _previousFocus = null;
    }
}
