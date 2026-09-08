using System;
using SeasonalPerks.UI.Audio;
using SeasonalPerks.UI.Media;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Controls;

public sealed class StoryVisitButton : Button
{
    private Image? _background;
    private Image? _icon;
    private Text? _label;

    public static StoryVisitButton Create(Transform parent, Font font, Action open, Action<InterfaceSound> sound)
    {
        // Live DialogueStartButton: centered in Tab Bar, 32px high, 16px type.
        var root = UiElements.Rect("Seasonal Visit", parent, 187, 32);
        root.anchorMin = root.anchorMax = new Vector2(.5f, 0);
        root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = new Vector2(0, 18);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var button = root.gameObject.AddComponent<StoryVisitButton>();
        button._background = UiElements.Fill(root, Color.white, true);
        button.targetGraphic = button._background;
        button._icon = UiElements.Rect("Dialogue icon", root, 36, 36, -43, 0).gameObject.AddComponent<Image>();
        button._icon.raycastTarget = false;
        var ui = new UiElements(font, sound);
        button._label = ui.Label(root, "Visit", "VISIT", 16, 90, 32, 16, 0);
        button._label.alignment = TextAnchor.MiddleCenter;
        button.onClick.AddListener(() => open());
        ui.Feedback(button);
        button.DoStateTransition(SelectionState.Normal, true);
        return button;
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        if (!_background || !_icon || !_label)
            return;
        var hover = state is SelectionState.Highlighted or SelectionState.Selected or SelectionState.Pressed;
        var shade =
            state == SelectionState.Pressed ? 1f
            : hover ? .802f
            : .575f;
        var alpha = state == SelectionState.Disabled ? .35f : 1f;
        _background!.sprite = StoryUiArtwork.Load(hover ? "visit-hover" : "visit-idle");
        _background.color = new Color(shade, shade, shade, alpha);
        _icon!.sprite = StoryUiArtwork.Load(hover ? "visit-icon-hover" : "visit-icon");
        _icon.color = new Color(1, 1, 1, alpha);
        _label!.color = hover ? new Color(0, 0, 0, alpha) : new Color(.84f, .85f, .85f, alpha);
    }
}
