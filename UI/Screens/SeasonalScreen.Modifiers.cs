using System;
using System.Linq;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonalScreen
{
    private void BuildModifiers()
    {
        // Live level44: 780 x 136 cards, 8px gutters, 16px heading gaps,
        // and 32px between sections, all within one scrolling viewport.
        var scroll = _ui.Scroll(_body, "ModifiersScroll", 1600, 0, 0, 0);
        var scrollRect = (RectTransform)scroll.transform;
        scrollRect.anchorMin = new Vector2(.5f, 0);
        scrollRect.anchorMax = new Vector2(.5f, 1);
        scrollRect.sizeDelta = new Vector2(1600, -32);
        scroll.GetComponent<Image>().color = Color.clear;
        UiElements.Stretch(scroll.viewport, 0, 32);
        var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 32;
        layout.padding = new RectOffset(0, 0, 16, 16);
        var track = (RectTransform)scroll.verticalScrollbar.transform;
        track.sizeDelta = new Vector2(6, -24);
        track.GetComponent<Image>().color = Color.clear;
        scroll.verticalScrollbar.targetGraphic.color = new Color(.714f, .757f, .780f);
        scroll.scrollSensitivity = 60;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        if (_state.ActiveMode != "seasonal" || _state.IsScav)
        {
            scroll.verticalScrollbar.gameObject.SetActive(false);
            var empty = _ui.Label(
                scroll.content,
                "NoModifiers",
                _state.IsScav ? "Seasonal modifiers do not apply to your Scav." : "Your normal character has no seasonal modifiers.",
                18,
                1568,
                80
            );
            empty.color = new Color(.584f, .620f, .639f);
            empty.alignment = TextAnchor.MiddleCenter;
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
            return;
        }

        var logo = UiElements.Fill(UiElements.Rect("SeasonLogo", _body, 262, 88), Color.white);
        logo.rectTransform.anchorMin = logo.rectTransform.anchorMax = Vector2.one;
        logo.rectTransform.pivot = new Vector2(1, 0);
        logo.rectTransform.anchoredPosition = new Vector2(-96, 0);
        ArtworkRequested?.Invoke("season-banner", logo);
        logo.preserveAspect = true;
        var active = _state.Perks.Where(perk => perk.Common ? perk.Enabled : _state.Selected.Contains(perk.Id)).ToArray();
        ModifierSection(scroll.content, "CommonModifiers", "COMMON MODIFIERS", active.Where(perk => perk.Common).ToArray());
        ModifierSection(
            scroll.content,
            "PositiveModifiers",
            "POSITIVE MODIFIERS",
            active.Where(perk => !perk.Common && perk.Points <= 0).ToArray()
        );
        ModifierSection(
            scroll.content,
            "NegativeModifiers",
            "NEGATIVE MODIFIERS",
            active.Where(perk => !perk.Common && perk.Points > 0).ToArray()
        );
    }

    private void ModifierSection(Transform parent, string name, string heading, PerkEntry[] perks)
    {
        if (perks.Length == 0)
        {
            return;
        }
        var section = UiElements.Rect(name, parent, 1568, 0);
        var layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 16;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var label = _ui.Label(section, "Heading", heading, 16, 1568, 18);
        label.color = new Color(.584f, .620f, .639f);
        label.alignment = TextAnchor.MiddleCenter;
        label.gameObject.AddComponent<LayoutElement>().preferredHeight = 18;
        for (var i = 0; i < perks.Length; i += 2)
        {
            var row = UiElements.Rect("Row", section, 1568, 136);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;
            var height = ModifierCard(row, perks[i]);
            if (i + 1 < perks.Length)
            {
                height = Mathf.Max(height, ModifierCard(row, perks[i + 1]));
            }
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            foreach (var cardLayout in row.GetComponentsInChildren<LayoutElement>().Where(value => value.transform.parent == row))
            {
                cardLayout.preferredHeight = height;
            }
        }
    }

    private float ModifierCard(Transform parent, PerkEntry perk)
    {
        var card = Object.Instantiate(_prefab(perk.Common ? "sharedassets44-1471" : "sharedassets44-3075"), parent, false);
        card.name = perk.Id;
        card.SetActive(true);
        DisableLayout(card);
        var root = (RectTransform)card.transform;
        root.sizeDelta = new Vector2(780, 136);
        var layout = card.GetComponent<LayoutElement>();
        layout.ignoreLayout = false;
        layout.minWidth = layout.preferredWidth = 780;
        layout.flexibleWidth = 0;
        var content = perk.Common ? root : (RectTransform)root.Find("Content");
        if (!perk.Common)
        {
            UiElements.Stretch(content);
            root.Find("StateContainers").gameObject.SetActive(false);
            content.Find("Lock").gameObject.SetActive(false);
        }
        var background = (RectTransform)content.Find("Background");
        UiElements.Stretch(background);
        foreach (Transform child in background)
        {
            child.gameObject.SetActive(false);
        }
        void Layer(string node, string artwork, Color color, bool tiled = false)
        {
            var image = background.Find(node).GetComponent<Image>();
            image.gameObject.SetActive(true);
            UiElements.Stretch(image.rectTransform);
            ArtworkRequested?.Invoke(artwork, image);
            image.color = color;
            image.type = tiled ? Image.Type.Tiled : Image.Type.Simple;
            image.raycastTarget = false;
        }
        var grey = new Color(.584f, .620f, .639f);
        Layer("BackgroundGradient", "modifier-gradient", grey);
        if (!perk.Common)
        {
            var node = perk.Points > 0 ? "Background_Idle_Negative" : "Background_Idle_Positive";
            Layer(node, "modifier-tint", perk.Points > 0 ? new Color(.831f, .161f, .161f, .220f) : new Color(.439f, .690f, .208f, .220f));
            var tint = (RectTransform)background.Find(node);
            tint.sizeDelta = new Vector2(-450, 0);
            tint.anchoredPosition = new Vector2(-225, 0);
        }
        Layer("BackgroundGrid", "modifier-grid", grey, true);
        Layer("BackgroundSadow", "modifier-shadow", Color.white);
        var iconRoot = (RectTransform)content.Find("Icon");
        Place(iconRoot, 136, 136, 68, -68, new Vector2(0, 1));
        var icon = iconRoot.Find("NetworkImageView").GetComponent<Image>();
        UiElements.Stretch(icon.rectTransform);
        foreach (Transform child in icon.transform)
        {
            child.gameObject.SetActive(false);
        }
        icon.gameObject.SetActive(true);
        icon.preserveAspect = true;
        IconRequested?.Invoke(perk.Id, icon);
        var info = (RectTransform)content.Find("Info");
        UiElements.Stretch(info, 136, 12, 10, 8);
        var title = info.Find(perk.Common ? "Name" : "NameContainer/Name").GetComponent<Text>();
        if (!perk.Common)
        {
            var names = (RectTransform)info.Find("NameContainer");
            UiElements.Stretch(names);
            names.Find("Points").gameObject.SetActive(false);
        }
        UiElements.Stretch(title.rectTransform);
        title.rectTransform.anchorMin = new Vector2(0, 1);
        title.rectTransform.pivot = new Vector2(.5f, 1);
        title.rectTransform.sizeDelta = new Vector2(0, 22);
        title.rectTransform.anchoredPosition = Vector2.zero;
        ConfigureText(
            title,
            perk.Name.ToUpperInvariant() + (perk.Common ? "" : " (" + (perk.Points > 0 ? "+" : "") + perk.Points + ")"),
            18,
            new Color(.851f, .851f, .851f)
        );
        var description = info.Find("Description").GetComponent<Text>();
        title.fontStyle = FontStyle.Bold;
        UiElements.Stretch(description.rectTransform, 8, 0, 28, 0);
        ConfigureText(description, perk.Description, 16, grey);
        description.alignment = TextAnchor.UpperLeft;
        if (!string.IsNullOrEmpty(perk.Unavailable))
        {
            description.text += "\nUnavailable: " + perk.Unavailable;
        }
        var height = Mathf.Max(136, description.preferredHeight + 54);
        layout.minHeight = layout.preferredHeight = height;
        return height;
    }
}
