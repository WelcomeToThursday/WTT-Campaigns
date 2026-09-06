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
    private void ReviewCreation()
    {
        if (_busy || _dialog != null || Created || !ValidSelection())
        {
            return;
        }
        HideTooltip();
        ClearCardHover();
        _dialog = Object.Instantiate(_prefab("level49-2761"), _panel, false);
        _dialog.name = "SaveModifiers";
        _dialog.SetActive(true);
        DisableLayout(_dialog);
        UiElements.Stretch((RectTransform)_dialog.transform, -100, -100, -100, -100);
        var firewall = (RectTransform)_dialog.transform.Find("Firewall ");
        UiElements.Stretch(firewall);
        UiElements.Fill(firewall, new Color(0, 0, 0, .6f), true);

        // Geometry and colors from SeasonalPersonalPerksConfirmationWindow (level49-2761).
        var window = (RectTransform)_dialog.transform.Find("Window");
        Place(window, 1000, 546, 0, 0);
        UiElements.Fill(window, new Color32(4, 5, 5, 250), true);
        foreach (Transform child in window)
        {
            child.gameObject.SetActive(false);
        }
        ConfirmationBorder(window, "Frame", new Color32(149, 158, 163, 110));
        var caption = UiElements.Rect("Caption", window, 996, 20, 0, 261);
        UiElements.Fill(caption, new Color32(84, 88, 91, 77));
        _ui.Label(caption, "Title", "Save modifiers", 14, 982, 18).color = Color.white;
        var description = _ui.Label(
            window,
            "SaveDescription",
            "Are you sure you want to save the selected modifiers?\nThis action cannot be undone.",
            18,
            936,
            59,
            0,
            204
        );
        description.alignment = TextAnchor.MiddleCenter;
        description.color = new Color32(197, 195, 178, 255);

        var scroll = _ui.Scroll(window, "SelectedModifiers", 936, 322, 0, -19);
        scroll.GetComponent<Image>().color = Color.clear;
        ConfirmationBorder(scroll.transform, "Border", new Color32(88, 93, 96, 51));
        UiElements.Stretch(scroll.viewport, 0, 14);
        var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 12, 12);
        layout.spacing = 26;
        scroll.verticalScrollbar.GetComponent<Image>().color = new Color32(88, 93, 96, 100);
        scroll.verticalScrollbar.targetGraphic.color = new Color32(197, 195, 178, 255);

        foreach (var positive in new[] { true, false })
        {
            var perks = _state.Perks.Where(perk => !perk.Common && _selected.Contains(perk.Id) && (perk.Points < 0) == positive).ToArray();
            if (perks.Length == 0)
            {
                continue;
            }
            var group = UiElements.Rect(positive ? "PositiveGroup" : "NegativeGroup", scroll.content, 922, 0);
            var rows = group.gameObject.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 2;
            rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            var header = UiElements.Rect("Header", group, 922, 28);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            var label = _ui.Label(header, "Label", (positive ? "POSITIVE" : "NEGATIVE") + " (" + perks.Length + ")", 18, 156, 28, -383);
            label.color = positive ? new Color32(99, 124, 76, 255) : new Color32(161, 72, 75, 255);
            var headerWidth = label.preferredWidth;
            Place(label.rectTransform, headerWidth, 28, -461 + headerWidth / 2, 0);
            UiElements.Fill(
                UiElements.Rect("Line", header, 922 - headerWidth - 16, 1, (headerWidth + 16) / 2, 0),
                new Color(1, 1, 1, .102f)
            );
            foreach (var perk in perks)
            {
                ConfirmationRow(group, perk);
            }
        }
        if (_selected.Count == 0)
        {
            scroll.verticalScrollbar.gameObject.SetActive(false);
            var empty = _ui.Label(scroll.transform, "Empty", "No modifiers selected", 18, 340, 20, 0, 20);
            empty.alignment = TextAnchor.MiddleCenter;
            empty.color = new Color32(95, 96, 96, 255);
            foreach (var y in new[] { -6, 46 })
            {
                UiElements.Fill(UiElements.Rect("Separator", scroll.transform, 20, 2, 0, y), new Color32(95, 96, 96, 76));
            }
        }

        var accepted = false;
        var accept = ConfirmationButton(
            window,
            "ACCEPT",
            -95,
            () =>
            {
                if (accepted || _busy || _dialog == null)
                {
                    return;
                }
                accepted = true;
                DismissDialog();
                CreateCharacter();
            }
        );
        var cancel = ConfirmationButton(window, "CANCEL", 95, DismissDialog);
        accept.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = cancel,
            selectOnRight = cancel,
        };
        cancel.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = accept,
            selectOnRight = accept,
        };
        if (EventSystem.current)
        {
            _previousFocus = EventSystem.current.currentSelectedGameObject;
            EventSystem.current.SetSelectedGameObject(cancel.gameObject);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.verticalNormalizedPosition = 1;
        scroll.verticalScrollbar.SetValueWithoutNotify(1);
    }

    private void ConfirmationRow(Transform parent, PerkEntry perk)
    {
        var row = Object.Instantiate(_prefab("level49-794"), parent, false);
        row.name = perk.Id;
        row.SetActive(true);
        DisableLayout(row);
        var rect = (RectTransform)row.transform;
        var description = row.transform.Find("Description").GetComponent<Text>();
        description.font = _ui.Font;
        description.fontSize = 15;
        description.text = perk.Description;
        description.color = new Color32(149, 158, 163, 178);
        Place(description.rectTransform, 630, 42, 142, 0);
        var height = Mathf.Max(50, description.preferredHeight + 8);
        Place(rect, 922, height, 0, 0);
        var layout = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
        layout.ignoreLayout = false;
        layout.minHeight = layout.preferredHeight = height;
        layout.flexibleHeight = 0;
        description.rectTransform.sizeDelta = new Vector2(630, height - 8);
        UiElements.Fill(rect, new Color32(12, 14, 14, 255));
        var nameContainer = (RectTransform)row.transform.Find("PerkNameContainer");
        Place(nameContainer, 234, height - 8, -340, 0);
        var name = nameContainer.Find("Name").GetComponent<Text>();
        name.font = _ui.Font;
        name.fontSize = 15;
        name.fontStyle = FontStyle.Bold;
        name.text = perk.Name.ToUpperInvariant();
        name.color = new Color32(149, 158, 163, 255);
        Place(name.rectTransform, 184, height - 8, 25, 0);
        var iconBackground = (RectTransform)nameContainer.Find("IconBackground");
        Place(iconBackground, 42, 42, -96, 0);
        UiElements.Fill(iconBackground, perk.Points < 0 ? new Color32(45, 67, 42, 255) : new Color32(77, 36, 39, 255));
        var border = iconBackground.Find("Border").GetComponent<Image>();
        ArtworkRequested?.Invoke("confirmation-border", border);
        border.type = Image.Type.Sliced;
        border.fillCenter = false;
        border.color = new Color(1, 1, 1, .18f);
        var iconContainer = (RectTransform)iconBackground.Find("Icon");
        iconContainer.gameObject.SetActive(true);
        UiElements.Stretch(iconContainer);
        UiElements.Fill(iconContainer, Color.clear);
        var icon = iconBackground.Find("Icon/Icon").GetComponent<Image>();
        icon.gameObject.SetActive(true);
        UiElements.Stretch(icon.rectTransform);
        icon.color = Color.clear;
        icon.preserveAspect = true;
        IconRequested?.Invoke(perk.Id, icon);
    }

    private void ConfirmationBorder(Transform parent, string name, Color color)
    {
        var image = UiElements.Fill(UiElements.Rect(name, parent, 0, 0), color);
        UiElements.Stretch(image.rectTransform);
        ArtworkRequested?.Invoke("confirmation-border", image);
        image.type = Image.Type.Sliced;
        image.fillCenter = false;
        image.color = color;
    }

    private Button ConfirmationButton(Transform parent, string caption, float x, System.Action action)
    {
        var button = _ui.Button(parent, caption, 150, x, -234, action, 43);
        button.targetGraphic.color = Color.clear;
        var label = button.GetComponentInChildren<Text>();
        label.fontSize = 24;
        label.color = new Color32(231, 229, 212, 255);
        button.targetGraphic = label;
        return button;
    }
}
