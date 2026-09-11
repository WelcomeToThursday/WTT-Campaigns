using System;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.Media;

namespace WTT.Campaigns.UI.Controls;

public sealed class StoryVisitButton : Button
{
    private Image? _background;
    private Image? _icon;
    private Text? _label;
    private bool _selected;
    private bool _reply;
    private bool _tab;
    private Image? _tabFill;
    private string _tabIcon = "visit-icon";
    private string _tabSelectedIcon = "visit-icon-hover";

    public void SetSelected(bool selected)
    {
        _selected = selected;
        DoStateTransition(currentSelectionState, true);
    }

    public void SetReplyMarker(Sprite sprite)
    {
        _icon!.sprite = sprite;
        DoStateTransition(currentSelectionState, true);
    }

    public static RectTransform CreateNavigation(Transform parent, Font font, Action buy, Action sell, Action<InterfaceSound> sound)
    {
        var tabs = UiElements.Rect("Visit trade tabs", parent, 400, 32);
        tabs.anchorMin = tabs.anchorMax = new Vector2(.5f, 1);
        tabs.anchoredPosition = new Vector2(0, -40);
        var purchase = CreateTab(tabs, font, "BUY", buy, sound);
        var sale = CreateTab(tabs, font, "SELL", sell, sound);
        var visit = CreateTab(tabs, font, "VISIT", () => { }, sound);
        LayoutTabs(new[] { purchase, sale, visit }, 400, 32);
        visit.SetSelected(true);
        return tabs;
    }

    public static StoryVisitButton CreateTab(Transform parent, Font font, string caption, Action action, Action<InterfaceSound> sound)
    {
        var button = Create(parent, font, action, sound);
        button._tab = true;
        button.name = caption == "VISIT" ? "Campaign Visit" : caption;
        button._background!.enabled = false;
        button._background.raycastTarget = false;

        var shadow = UiElements.Rect("Tab shadow", button.transform, 49, 30);
        shadow.anchorMin = shadow.anchorMax = new Vector2(1, .5f);
        shadow.pivot = new Vector2(0, .5f);
        shadow.anchoredPosition = new Vector2(-27, 0);
        var shadowImage = shadow.gameObject.AddComponent<Image>();
        shadowImage.sprite = StoryUiArtwork.Load("tab-shadow");
        shadowImage.raycastTarget = false;

        // Use the Trading / Tasks / Services construction: sliced mask and outline,
        // tiled fill, and a 25 px overlap. Never stretch its diagonal edge or texture.
        Image Layer(string name, Transform container, bool hit = false)
        {
            var rect = UiElements.Rect(name, container, 0, 0);
            UiElements.Stretch(rect);
            return UiElements.Fill(rect, Color.white, hit);
        }
        var mask = Layer("Tab mask", button.transform, true);
        mask.sprite = StoryUiArtwork.Load("tab-mask", new Vector4(13, 0, 36, 0));
        mask.type = Image.Type.Sliced;
        mask.alphaHitTestMinimumThreshold = .1f;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        button._tabFill = Layer("Tab fill", mask.transform);
        button._tabFill.type = Image.Type.Tiled;
        button._background = Layer("Tab outline", button.transform);
        button._background.type = Image.Type.Sliced;
        button.targetGraphic = mask;
        button._icon!.transform.SetAsLastSibling();
        button._label!.transform.SetAsLastSibling();
        button._label.text = caption;
        button._label.fontSize = 16;
        button._label.alignment = TextAnchor.MiddleLeft;
        UiElements.Stretch(button._label.rectTransform, 51, 28, 1, 1);
        var icon = button._icon.rectTransform;
        icon.anchorMin = icon.anchorMax = new Vector2(0, .5f);
        icon.pivot = new Vector2(0, .5f);
        icon.anchoredPosition = new Vector2(24, 0);
        icon.sizeDelta = new Vector2(24, 24);
        button._icon.preserveAspect = true;
        if (caption == "BUY" || caption == "SELL")
        {
            button._tabIcon = "tab-" + caption.ToLowerInvariant() + "-icon";
            button._tabSelectedIcon = button._tabIcon + "-selected";
        }
        button.DoStateTransition(SelectionState.Normal, true);
        return button;
    }

    public static void LayoutTabs(StoryVisitButton[] buttons, float width, float height)
    {
        var overlap = 25f;
        var tabWidth = (width + overlap * (buttons.Length - 1)) / buttons.Length;
        for (var index = 0; index < buttons.Length; index++)
        {
            var rect = (RectTransform)buttons[index].transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
            rect.pivot = new Vector2(0, .5f);
            rect.anchoredPosition = new Vector2(index * (tabWidth - overlap), 0);
            rect.sizeDelta = new Vector2(tabWidth, height);
            // The tab on the left covers the following tab's slanted leading edge.
            var sibling = buttons.Length - index - 1;
            if (rect.GetSiblingIndex() != sibling)
                rect.SetSiblingIndex(sibling);
        }
    }

    public static StoryVisitButton Create(Transform parent, Font font, Action open, Action<InterfaceSound> sound)
    {
        // Recovered Tarkov chat-tab artwork is shared by every visit navigation control.
        var root = UiElements.Rect("Campaign Visit", parent, 187, 32);
        root.anchorMin = root.anchorMax = new Vector2(.5f, 0);
        root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = new Vector2(0, 18);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var button = root.gameObject.AddComponent<StoryVisitButton>();
        button._background = UiElements.Fill(root, Color.white, true);
        button.targetGraphic = button._background;
        button._icon = UiElements.Rect("Dialogue icon", root, 22, 22, -32, 0).gameObject.AddComponent<Image>();
        button._icon.raycastTarget = false;
        var ui = new UiElements(font, sound);
        button._label = ui.Label(root, "Visit", "VISIT", 18, 90, 32, 16, 0);
        button._label.alignment = TextAnchor.MiddleCenter;
        UiElements.Stretch(button._label.rectTransform, 48, 16, 2, 2);
        button.onClick.AddListener(() => open());
        ui.Feedback(button);
        button.DoStateTransition(SelectionState.Normal, true);
        return button;
    }

    public static StoryVisitButton CreateAction(
        Transform parent,
        Font font,
        string caption,
        float width,
        float x,
        float y,
        Action action,
        float height,
        Action<InterfaceSound>? sound,
        bool reply = false
    )
    {
        var button = Create(parent, font, action, sound ?? (_ => { }));
        button.GetComponent<LayoutElement>().ignoreLayout = false;
        button.name = caption;
        button._reply = reply;
        button._icon!.gameObject.SetActive(false);
        button._label!.text = reply ? caption : caption.ToUpperInvariant();
        button._label.fontSize = height <= 30 ? 16 : 18;
        button._label.rectTransform.anchoredPosition = Vector2.zero;
        UiElements.Stretch(button._label.rectTransform, reply ? 34 : 16, 16, 2, 2);
        button._label.alignment = reply ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
        if (reply)
        {
            // TraderDialogWindowOptionRow uses italic dialogue text, an unpainted
            // idle hit area and a warm selection highlight, not a button strip.
            button._label.fontStyle = FontStyle.Italic;
            UiElements.Stretch(button._label.rectTransform, 33, 10, 5, 8);
            button._icon.gameObject.SetActive(true);
            button._icon.name = "Reply marker";
            button._icon.sprite = StoryUiArtwork.Load("reply");
            button._icon.preserveAspect = true;
            var marker = button._icon.rectTransform;
            marker.anchorMin = marker.anchorMax = new Vector2(0, 1);
            marker.pivot = new Vector2(0, 1);
            marker.anchoredPosition = new Vector2(10, -7);
            marker.sizeDelta = new Vector2(18, 18);
        }
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        button.DoStateTransition(SelectionState.Normal, true);
        return button;
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        if (!_background || !_icon || !_label)
            return;
        if (_reply)
        {
            var focused = state is SelectionState.Highlighted or SelectionState.Selected or SelectionState.Pressed;
            var opacity = state == SelectionState.Disabled ? .35f : 1f;
            _background!.sprite = null;
            _background.color = focused ? new Color(.6353f, .62745f, .56863f, opacity) : Color.clear;
            _label!.color = focused ? new Color(0, 0, 0, opacity) : new Color(1, .95686f, .82353f, opacity);
            _icon!.color = focused ? new Color(0, 0, 0, opacity) : new Color(1, 1, 1, .62353f * opacity);
            return;
        }
        if (_tab)
        {
            var selected = _selected;
            var highlighted = state is SelectionState.Highlighted or SelectionState.Pressed;
            var key =
                selected ? "selected"
                : highlighted ? "hover"
                : "idle";
            var opacity = state == SelectionState.Disabled ? .35f : 1f;
            _tabFill!.sprite = StoryUiArtwork.Load("tab-" + key + "-fill");
            _tabFill.color = new Color(1, 1, 1, opacity);
            _background!.sprite = StoryUiArtwork.Load("tab-" + key + "-outline", new Vector4(9, 0, 33, 0));
            _background.color = new Color(1, 1, 1, opacity);
            _icon!.sprite = StoryUiArtwork.Load(selected ? _tabSelectedIcon : _tabIcon);
            _icon.color = selected && _tabIcon != "visit-icon" ? new Color(0, 0, 0, opacity) : new Color(1, 1, 1, opacity);
            _label!.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            _label.color = selected ? new Color(0, 0, 0, opacity) : new Color(.78f, .827f, .851f, opacity);
            return;
        }
        var hover = _selected || state is SelectionState.Highlighted or SelectionState.Selected or SelectionState.Pressed;
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
