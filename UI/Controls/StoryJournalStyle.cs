using System;
using SeasonalPerks.UI.Media;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Controls;

public static class StoryJournalStyle
{
    // Live level44 MainQuestPanel and its sharedassets44 presentation prefabs.
    public static readonly Color Text = new Color32(237, 235, 214, 255);
    public static readonly Color Caption = new Color32(168, 177, 181, 255);
    public static readonly Color Border = new Color32(55, 58, 52, 255);
    public static readonly Color Active = new Color32(231, 141, 25, 255);
    public static readonly Color Complete = new Color32(117, 185, 222, 255);
    public static readonly Color Failed = new Color32(231, 54, 54, 255);

    public static Image Artwork(RectTransform rect, string name, bool hit = false)
    {
        var border =
            name == "journal-rail" ? new Vector4(0, 30, 0, 769)
            : name == "journal-border" ? Vector4.one
            : name == "journal-objectiveprogressback" ? new Vector4(4, 3, 4, 3)
            : Vector4.zero;
        var image = UiElements.Fill(rect, Color.white, hit);
        image.sprite = StoryUiArtwork.Load(name, border);
        image.type = border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
        // Do not stretch/filter the border sprite's single transparent center
        // texel across the panel; the native pixel-perfect border draws edges.
        if (name == "journal-border")
            image.fillCenter = false;
        return image;
    }

    public static void Outline(RectTransform root)
    {
        var rect = UiElements.Rect("Border", root, 0, 0);
        UiElements.Stretch(rect);
        Artwork(rect, "journal-border").color = Border;
    }

    public static ScrollRect Scroll(
        UiElements ui,
        Transform parent,
        string name,
        float width,
        float height,
        float x,
        float y,
        RectOffset padding
    )
    {
        var scroll = ui.Scroll(parent, name, width, height, x, y);
        scroll.GetComponent<Image>().color = Color.clear;
        var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
        layout.padding = padding;
        layout.spacing = 10;
        var bar = scroll.verticalScrollbar;
        bar.GetComponent<Image>().color = new Color32(31, 33, 34, 255);
        bar.targetGraphic.color = new Color32(65, 65, 65, 255);
        var rect = (RectTransform)bar.transform;
        rect.sizeDelta = new Vector2(10, -4);
        rect.anchoredPosition = new Vector2(-5, 0);
        var track = UiElements.Rect("Scrollbar interior", rect, 0, 0);
        UiElements.Stretch(track, 1, 1, 1, 1);
        UiElements.Fill(track, new Color32(1, 1, 1, 255));
        track.SetAsFirstSibling();
        var sliding = UiElements.Rect("Sliding area", rect, 0, 0);
        UiElements.Stretch(sliding, 2, 2, 2, 2);
        bar.enabled = false;
        bar.handleRect.SetParent(sliding, false);
        UiElements.Stretch(bar.handleRect);
        bar.enabled = true;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return scroll;
    }

    public static void FitScrollbar(ScrollRect? scroll)
    {
        if (scroll)
            scroll!.verticalScrollbar.gameObject.SetActive(scroll.content.rect.height > scroll.viewport.rect.height + .1f);
    }

    public static Button Expand(RectTransform parent, string name, bool expanded, float x, float y, Action click)
    {
        var rect = UiElements.Rect(name, parent, 44, 36, x, y);
        var button = rect.gameObject.AddComponent<Button>();
        UiElements.Fill(rect, Color.clear, true);
        var icon = Artwork(UiElements.Rect("Expand arrow", rect, 28, 20), "journal-expand");
        var colors = button.colors;
        colors.normalColor = new Color(1, 1, 1, expanded ? 1 : .5f);
        colors.highlightedColor = Color.white;
        button.colors = colors;
        button.targetGraphic = icon;
        button.onClick.AddListener(() => click());
        return button;
    }

    public static void Unread(Transform parent, float x, float y)
    {
        Artwork(UiElements.Rect("Unread marker", parent, 34, 32, x, y), "journal-unread");
    }
}
