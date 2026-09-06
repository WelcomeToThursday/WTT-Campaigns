using System;
using SeasonalPerks.UI.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Controls;

public sealed class UiElements
{
    public static readonly Color Ink = new Color(.84f, .82f, .73f);
    public static readonly Color Muted = new Color(.55f, .55f, .50f);
    public static readonly Color Accent = new Color(.67f, .63f, .45f);
    public static readonly Color Positive = new Color(.52f, .70f, .37f);
    public static readonly Color Negative = new Color(.78f, .40f, .36f);
    public readonly Font Font;
    private readonly Action<InterfaceSound>? _sound;

    public UiElements(Font font, Action<InterfaceSound>? sound = null)
    {
        Font = font;
        _sound = sound;
    }

    public void Feedback(Button button, InterfaceSound click = InterfaceSound.ButtonClick, Func<bool>? allowed = null)
    {
        var feedback = button.GetComponent<UiButtonFeedback>() ?? button.gameObject.AddComponent<UiButtonFeedback>();
        feedback.Initialize(button, sound => _sound?.Invoke(sound), click, allowed);
    }

    public static void Destroy(UnityEngine.Object value)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(value);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(value);
        }
    }

    public static RectTransform Rect(string name, Transform parent, float width, float height, float x = 0, float y = 0)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
        return rect;
    }

    public static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
    }

    public static Image Fill(RectTransform rect, Color color, bool hit = false)
    {
        var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.sprite = null;
        image.color = color;
        image.raycastTarget = hit;
        return image;
    }

    public Text Label(Transform parent, string name, string value, int size, float width, float height, float x = 0, float y = 0)
    {
        var text = Rect(name, parent, width, height, x, y).gameObject.AddComponent<Text>();
        text.font = Font;
        text.fontSize = size;
        text.color = Ink;
        text.text = value;
        text.supportRichText = false;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    public Button Button(
        Transform parent,
        string caption,
        float width,
        float x,
        float y,
        Action action,
        float height = 44,
        InterfaceSound clickSound = InterfaceSound.ButtonClick
    )
    {
        var rect = Rect(caption, parent, width, height, x, y);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = Fill(rect, new Color(.18f, .18f, .15f), true);
        var colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.4f, 1.25f);
        colors.pressedColor = new Color(.65f, .65f, .55f);
        colors.disabledColor = new Color(.5f, .5f, .5f, .6f);
        button.colors = colors;
        Feedback(button, clickSound);
        button.onClick.AddListener(() => action());
        Label(rect, "Label", caption, 18, width - 20, height - 4).alignment = TextAnchor.MiddleCenter;
        return button;
    }

    public InputField Input(Transform parent, string name, string placeholder, float width, float x, float y)
    {
        var rect = Rect(name, parent, width, 42, x, y);
        Fill(rect, new Color(.095f, .095f, .078f), true);
        var input = rect.gameObject.AddComponent<InputField>();
        input.textComponent = Label(rect, "Value", "", 19, width - 26, 36);
        input.placeholder = Label(rect, "Placeholder", placeholder, 18, width - 26, 36);
        input.placeholder.color = Muted;
        input.characterLimit = 60;
        return input;
    }

    public ScrollRect Scroll(Transform parent, string name, float width, float height, float x, float y)
    {
        var rect = Rect(name, parent, width, height, x, y);
        Fill(rect, new Color(.035f, .038f, .032f), true);
        var viewport = Rect("Viewport", rect, 0, 0);
        Stretch(viewport, 0, 17);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport, 0, 0);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10;
        layout.padding = new RectOffset(4, 4, 4, 12);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var track = Rect("Scrollbar", rect, 8, 0);
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = Vector2.one;
        track.anchoredPosition = new Vector2(-5, 0);
        track.sizeDelta = new Vector2(8, -8);
        Fill(track, new Color(.10f, .10f, .085f), true);
        var handle = Rect("Handle", track, 0, 0);
        Stretch(handle);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.targetGraphic = Fill(handle, new Color(.39f, .38f, .30f), true);
        scrollbar.handleRect = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var scroll = rect.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 38;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.verticalScrollbar = scrollbar;
        return scroll;
    }
}
