using System;
using SeasonalPerks.UI.Audio;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed class StoryConversationPanel
{
    private readonly UiElements _ui;
    private readonly RectTransform _root;
    private readonly Action<string> _select;
    private readonly Action _close;
    private readonly Action _skip;
    private string _trader = "";
    private string _text = "";
    private string _history = "";
    private StoryReplyView[] _choices = Array.Empty<StoryReplyView>();
    private bool _showHistory;
    private bool _busy;
    private StoryReplyView? _confirmation;
    private readonly Sprite? _replyIcon;
    private bool _canSkip;

    public StoryConversationPanel(
        Transform parent,
        Font font,
        Action<string> select,
        Action close,
        Action skip,
        Sprite? replyIcon = null,
        Action<InterfaceSound>? sound = null
    )
    {
        _ui = new UiElements(font, sound);
        _replyIcon = replyIcon;
        _select = select;
        _close = close;
        _skip = skip;
        _root = UiElements.Rect("Story conversation", parent, 800, 170);
        _root.anchorMin = _root.anchorMax = new Vector2(.5f, 0);
        _root.pivot = new Vector2(.5f, 0);
        _root.anchoredPosition = new Vector2(0, 65);
    }

    public void Set(string trader, string text, string history, StoryReplyView[] choices, bool busy, bool canSkip = false)
    {
        _trader = trader;
        _text = text;
        _history = history;
        _choices = choices;
        _busy = busy;
        _canSkip = canSkip;
        _confirmation = null;
        Render();
    }

    public void SetBusy(bool busy, bool canSkip)
    {
        if (_busy == busy && _canSkip == canSkip)
            return;
        _busy = busy;
        _canSkip = canSkip;
        Render();
    }

    private void Render()
    {
        for (var index = _root.childCount - 1; index >= 0; index--)
        {
            var child = _root.GetChild(index);
            child.gameObject.SetActive(false);
            UiElements.Destroy(child.gameObject);
        }
        var value = _confirmation?.Confirmation ?? (_showHistory ? _history : _text);
        var measure = _ui.Label(_root, "Measure", value, 18, 770, 0);
        var messageHeight = string.IsNullOrWhiteSpace(value) ? 0 : Math.Max(32, measure.preferredHeight + 12);
        UiElements.Destroy(measure.gameObject);
        var repliesHeight = 0f;
        foreach (var choice in _choices)
            repliesHeight += RowHeight(choice.Text) + 3;
        if (_confirmation != null)
            repliesHeight = 72;
        var available = ((RectTransform)_root.parent).rect.height - 150;
        var height = Math.Min(Math.Max(140, available), Math.Max(84, 54 + messageHeight + repliesHeight));
        _root.sizeDelta = new Vector2(800, height);
        UiElements.Fill(_root, new Color(.035f, .045f, .05f, .94f), true);
        var border = new Color(.322f, .349f, .353f);
        UiElements.Fill(UiElements.Rect("Top border", _root, 800, 1, 0, height / 2), border);
        UiElements.Fill(UiElements.Rect("Bottom border", _root, 800, 1, 0, -height / 2), border);
        UiElements.Fill(UiElements.Rect("Left border", _root, 1, height, -400, 0), border);
        UiElements.Fill(UiElements.Rect("Right border", _root, 1, height, 400, 0), border);
        _ui.Label(_root, "Trader", _trader, 20, 530, 30, -120, height / 2 - 20).color = new Color(.843f, .851f, .851f);
        var history = _ui.Button(
            _root,
            _showHistory ? "Hide history" : "Show history",
            145,
            313,
            height / 2 - 20,
            () =>
            {
                _showHistory = !_showHistory;
                Render();
            },
            28
        );
        history.interactable = _history.Length > 0;
        history.gameObject.SetActive(_history.Length > 0);
        UiElements.Fill(UiElements.Rect("Header separator", _root, 778, 1, 0, height / 2 - 39), border);
        var viewport = UiElements.Rect("Conversation viewport", _root, 778, height - 50, 0, -20);
        UiElements.Fill(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.scrollSensitivity = 38;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var content = UiElements.Rect("Conversation content", viewport, 778, 0);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;
        if (messageHeight > 0)
        {
            var message = _ui.Label(content, "Dialogue", value, 18, 770, messageHeight);
            message.color = new Color(.843f, .851f, .851f);
            message.gameObject.AddComponent<LayoutElement>().preferredHeight = messageHeight;
        }
        if (_confirmation != null)
        {
            var chosen = _confirmation;
            Reply(content, "Confirm", () => _select(chosen.Id), !_busy);
            Reply(
                content,
                "Cancel",
                () =>
                {
                    _confirmation = null;
                    Render();
                },
                !_busy
            );
        }
        else
        {
            foreach (var choice in _choices)
            {
                var reply = choice;
                Reply(
                    content,
                    reply.Text,
                    () =>
                    {
                        if (reply.Confirmation.Length > 0)
                        {
                            _confirmation = reply;
                            Render();
                        }
                        else
                            _select(reply.Id);
                    },
                    !_busy
                );
            }
        }
        if (_canSkip)
            _ui.Button(_root, "Skip playback", 145, -327, -height / 2 - 24, _skip, 28);
        _ui.Button(_root, "Leave  [ESC]", 145, 327, -height / 2 - 24, _close, 28);
    }

    private static float RowHeight(string text) => Math.Max(33, 24 * (1 + text.Length / 75));

    private void Reply(Transform parent, string text, Action action, bool enabled)
    {
        var height = RowHeight(text);
        var button = _ui.Button(parent, text, 778, 0, 0, action, height);
        button.interactable = enabled;
        button.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        button.targetGraphic.color = new Color(.12f, .13f, .13f, .7f);
        var label = button.GetComponentInChildren<Text>();
        label.alignment = TextAnchor.MiddleLeft;
        label.color = new Color(1, .957f, .824f);
        label.rectTransform.sizeDelta = new Vector2(736, height - 2);
        label.rectTransform.anchoredPosition = new Vector2(10, 0);
        if (_replyIcon)
        {
            var icon = UiElements.Rect("Reply marker", button.transform, 18, 18, -376, 0).gameObject.AddComponent<Image>();
            icon.sprite = _replyIcon;
            icon.color = new Color(1, 1, 1, .624f);
            icon.raycastTarget = false;
        }
    }
}
