using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeasonalPerks.UI;

public sealed partial class SeasonalScreen
{
    private void BuildPerks()
    {
        var personal = Page == ScreenPage.Personal;
        var common = Page == ScreenPage.Global;
        if (personal && !Created)
        {
            _ui.Label(_body, "NicknameLabel", "NICKNAME", 15, 110, 32, -805, 270).color =
                UiElements.Muted;
            _nickname = _ui.Input(_body, "Nickname", "Seasonal", 300, -590, 270);
            _nickname.text = _name;
            _nickname.characterLimit = 15;
            _nickname.contentType = InputField.ContentType.Alphanumeric;
            _nickname.onValueChanged.AddListener(value => _name = value);
            _ui.Label(_body, "FactionLabel", "FACTION", 15, 100, 32, -355, 270).color =
                UiElements.Muted;
            Button? usec = null;
            Button? bear = null;
            void Faction(string side)
            {
                _side = side;
                ((Image)usec!.targetGraphic).color =
                    side == "Usec" ? new Color(.35f, .33f, .23f) : new Color(.14f, .14f, .12f);
                ((Image)bear!.targetGraphic).color =
                    side == "Bear" ? new Color(.35f, .33f, .23f) : new Color(.14f, .14f, .12f);
            }
            usec = _ui.Button(_body, "USEC", 115, -245, 270, () => Faction("Usec"), 42);
            bear = _ui.Button(_body, "BEAR", 115, -115, 270, () => Faction("Bear"), 42);
            Faction(_side);
        }
        else
        {
            _ui.Label(
                _body,
                "Context",
                common ? "GLOBAL RULES"
                    : Page == ScreenPage.Summary ? "ACTIVE MODIFIERS"
                    : "SEASONAL CHARACTER",
                17,
                700,
                42,
                -510,
                270
            ).color = UiElements.Muted;
        }
        _search = _ui.Input(_body, "Search", "Search perks...", 385, 662, 270);
        _search.onValueChanged.AddListener(value =>
        {
            _query = value.Trim();
            FilterCards(true);
        });
        var left = _ui.Scroll(_body, "Detrimental", 850, 492, -440, -42);
        var right = _ui.Scroll(_body, "Beneficial", 850, 492, 440, -42);
        _ui.Label(
            _body,
            "LeftHeading",
            common ? "GLOBAL MODIFIERS" : "DETRIMENTAL",
            19,
            650,
            32,
            -530,
            217
        ).color = common ? UiElements.Accent : UiElements.Negative;
        _ui.Label(
            _body,
            "RightHeading",
            common ? "GLOBAL MODIFIERS" : "BENEFICIAL",
            19,
            650,
            32,
            350,
            217
        ).color = common ? UiElements.Accent : UiElements.Positive;
        var entries = _state.Perks.Where(perk => common ? perk.Common : !perk.Common);
        if (Page == ScreenPage.Summary)
        {
            entries =
                _state.ActiveMode == "seasonal"
                    ? _state.Perks.Where(perk =>
                        perk.Common ? perk.Enabled : _state.Selected.Contains(perk.Id)
                    )
                    : Array.Empty<PerkEntry>();
        }
        var ordered = entries
            .OrderBy(perk => perk.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var index = 0;
        foreach (var perk in ordered)
        {
            var column =
                common ? (index++ % 2 == 0 ? left : right)
                : perk.Points > 0 ? left
                : right;
            var card = BuildCard(perk, column.content);
            _cards.Add((perk, card));
        }
        foreach (var scroll in new[] { left, right })
        {
            var placeholder = _ui.Label(
                scroll.content,
                "Empty",
                Page == ScreenPage.Summary
                    ? "No active modifiers in this group."
                    : "No matching perks.",
                21,
                780,
                115
            );
            placeholder.color = UiElements.Muted;
            placeholder.gameObject.AddComponent<LayoutElement>().preferredHeight = 115;
        }
        RefreshCards();
    }

    private GameObject BuildCard(PerkEntry perk, Transform parent)
    {
        var card = Object.Instantiate(_prefab("level47-82"), parent, false);
        card.name = perk.Id;
        card.SetActive(true);
        DisableLayout(card);
        var root = (RectTransform)card.transform;
        root.sizeDelta = new Vector2(805, 160);
        var layout = card.GetComponent<LayoutElement>() ?? card.AddComponent<LayoutElement>();
        layout.enabled = true;
        layout.minHeight = layout.preferredHeight = 160;
        layout.flexibleHeight = 0;
        layout.preferredWidth = -1;
        var content = root.Find("Content");
        UiElements.Stretch((RectTransform)content);
        var background = content.Find("Background");
        UiElements.Stretch((RectTransform)background);
        foreach (Transform child in background)
        {
            child.gameObject.SetActive(false);
        }
        var hover = CardArtwork(card, perk);
        var iconRect = (RectTransform)content.Find("Icon");
        Place(iconRect, 104, 104, 66, 0, new Vector2(0, .5f));
        var icon = iconRect
            .GetComponentsInChildren<Image>(true)
            .First(image => image.name == "NetworkImageView");
        UiElements.Stretch(icon.rectTransform);
        icon.gameObject.SetActive(true);
        icon.color = new Color(1, 1, 1, .08f);
        icon.preserveAspect = true;
        var info = (RectTransform)content.Find("Info");
        UiElements.Stretch(info, 134, 72, 14, 12);
        var nameContainer = (RectTransform)info.Find("NameContainer");
        nameContainer.anchorMin = new Vector2(0, 1);
        nameContainer.anchorMax = Vector2.one;
        nameContainer.pivot = new Vector2(.5f, 1);
        nameContainer.sizeDelta = new Vector2(0, 44);
        nameContainer.anchoredPosition = Vector2.zero;
        var name = nameContainer.Find("Name").GetComponent<Text>();
        UiElements.Stretch(name.rectTransform);
        ConfigureText(name, perk.Name, 22, UiElements.Ink);
        nameContainer.Find("Points").gameObject.SetActive(false);
        var description = info.Find("Description").GetComponent<Text>();
        UiElements.Stretch(description.rectTransform, 0, 0, 46, 18);
        ConfigureText(description, perk.Description, 17, UiElements.Muted);
        description.alignment = TextAnchor.UpperLeft;
        var state = _ui.Label(content, "SelectionState", "", 13, 520, 19, 0, -65);
        state.rectTransform.anchorMin = state.rectTransform.anchorMax = new Vector2(0, .5f);
        state.rectTransform.pivot = new Vector2(0, .5f);
        state.rectTransform.anchoredPosition = new Vector2(134, -65);
        var points = _ui.Label(
            content,
            "SignedPoints",
            perk.Common ? "" : (perk.Points > 0 ? "+" : "") + perk.Points,
            25,
            58,
            46
        );
        Place(points.rectTransform, 58, 46, -36, 36, new Vector2(1, .5f));
        points.alignment = TextAnchor.MiddleCenter;
        points.color = perk.Points > 0 ? UiElements.Negative : UiElements.Positive;
        var mark = _ui.Label(content, "SelectionMark", "", 22, 58, 46);
        Place(mark.rectTransform, 58, 46, -36, -25, new Vector2(1, .5f));
        mark.alignment = TextAnchor.MiddleCenter;
        content.Find("Lock").gameObject.SetActive(false);
        root.Find("StateContainers").gameObject.SetActive(false);
        var stripe = UiElements.Rect("Stripe", content, 3, 0);
        stripe.anchorMin = Vector2.zero;
        stripe.anchorMax = new Vector2(0, 1);
        stripe.anchoredPosition = new Vector2(2, 0);
        UiElements.Fill(
            stripe,
            perk.Common ? UiElements.Accent
                : perk.Points > 0 ? UiElements.Negative
                : UiElements.Positive
        );
        var button = card.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = UiElements.Fill(root, Color.clear, true);
        _ui.Feedback(
            button,
            InterfaceSound.None,
            () =>
                PersonalPage
                && !perk.Common
                && !_busy
                && _dialog == null
                && (_state.AllowEdits || !Created)
        );
        button.onClick.AddListener(() => Toggle(perk));
        hover.Entered = () =>
        {
            if (icon.sprite == null)
            {
                IconRequested?.Invoke(perk.Id, icon);
            }
        };
        IconRequested?.Invoke(perk.Id, icon);
        return card;
    }

    private void RefreshCards()
    {
        foreach (var pair in _cards)
        {
            var perk = pair.Entry;
            var chosen =
                perk.Common ? perk.Enabled
                : Page == ScreenPage.Summary ? _state.Selected.Contains(perk.Id)
                : _selected.Contains(perk.Id);
            var reason = PersonalPage ? LockReason(perk) : perk.Unavailable;
            var locked = reason.Length > 0;
            pair.Card.GetComponent<PerkCardHover>().Refresh(!perk.Common && chosen);
            var state = pair
                .Card.GetComponentsInChildren<Text>(true)
                .First(text => text.name == "SelectionState");
            state.text =
                locked ? (!string.IsNullOrEmpty(perk.Unavailable) ? "UNAVAILABLE" : "CONFLICT")
                : chosen
                    ? (
                        perk.Common ? "ACTIVE GLOBAL RULE"
                        : CreationPage ? ""
                        : "SELECTED"
                    )
                : perk.Common ? "DISABLED ON SERVER"
                : "";
            state.color = locked ? UiElements.Negative : UiElements.Positive;
            pair
                .Card.GetComponentsInChildren<Text>(true)
                .First(text => text.name == "SelectionMark")
                .text = chosen ? "[x]" : "[ ]";
        }
        FilterCards();
        RefreshFooter();
    }

    private void FilterCards(bool resetScroll = false)
    {
        HideTooltip();
        foreach (var pair in _cards)
        {
            pair.Card.SetActive(
                _query.Length == 0
                    || pair.Entry.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0
                    || pair.Entry.Description.IndexOf(_query, StringComparison.OrdinalIgnoreCase)
                        >= 0
            );
        }
        foreach (var scroll in _body.GetComponentsInChildren<ScrollRect>())
        {
            var empty = scroll.content.Find("Empty");
            if (empty)
            {
                empty.gameObject.SetActive(
                    !_cards.Any(pair =>
                        pair.Card.transform.parent == scroll.content && pair.Card.activeSelf
                    )
                );
            }
            if (resetScroll)
            {
                scroll.verticalNormalizedPosition = 1;
            }
        }
    }

    private static void DisableLayout(GameObject gameObject)
    {
        foreach (var layout in gameObject.GetComponentsInChildren<LayoutGroup>(true))
        {
            layout.enabled = false;
        }
        foreach (var fitter in gameObject.GetComponentsInChildren<ContentSizeFitter>(true))
        {
            fitter.enabled = false;
        }
        foreach (var group in gameObject.GetComponentsInChildren<CanvasGroup>(true))
        {
            group.alpha = 1;
            group.blocksRaycasts = true;
        }
    }

    private void ConfigureText(Text text, string value, int size, Color color)
    {
        text.font = _ui.Font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.supportRichText = false;
        text.raycastTarget = false;
    }

    private static void Place(
        RectTransform rect,
        float width,
        float height,
        float x,
        float y,
        Vector2? anchor = null
    )
    {
        rect.anchorMin = rect.anchorMax = anchor ?? new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
        rect.localScale = Vector3.one;
    }
}
