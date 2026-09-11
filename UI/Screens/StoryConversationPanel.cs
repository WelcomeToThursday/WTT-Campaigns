using System;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;

namespace WTT.Campaigns.UI.Screens;

public sealed class StoryConversationPanel
{
    private readonly UiElements _ui;
    private readonly Action<InterfaceSound>? _sound;
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
    private Vector2 _parentSize;
    private float _width = 800;

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
        _sound = sound;
        _replyIcon = replyIcon;
        _select = select;
        _close = close;
        _skip = skip;
        _root = UiElements.Rect("Story conversation", parent, 800, 170);
        _root.anchorMin = _root.anchorMax = new Vector2(.5f, 0);
        _root.pivot = new Vector2(.5f, 0);
        _root.anchoredPosition = new Vector2(0, 32);
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

    public void RefreshLayout()
    {
        if (_root && ((RectTransform)_root.parent).rect.size != _parentSize)
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
        _parentSize = ((RectTransform)_root.parent).rect.size;
        _width = Math.Max(360, Math.Min(980, _parentSize.x - 64));
        var value = _confirmation?.Confirmation ?? (_showHistory ? _history : _text);
        var messageHeight = string.IsNullOrWhiteSpace(value) ? 0 : Measure(value, _width - 54);
        var repliesHeight = 0f;
        foreach (var choice in _choices)
            repliesHeight += RowHeight(choice.Text) + 3;
        if (_confirmation != null)
            repliesHeight = 72;
        var available = Math.Max(180, _parentSize.y * .48f - 32);
        // Reserve separate, bounded areas for text and replies. Long history
        // cannot displace the response controls or cover the trader's face.
        var textArea = Math.Min(messageHeight + (messageHeight > 0 ? 12 : 0), available * .48f);
        var replyArea = Math.Min(repliesHeight + 16, available - textArea - 92);
        replyArea = Math.Max(40, replyArea);
        var height = 92 + textArea + replyArea;
        _root.sizeDelta = new Vector2(_width, height);
        UiElements.Fill(_root, new Color(.035f, .045f, .05f, .94f), true);
        var border = new Color(.322f, .349f, .353f);
        UiElements.Fill(UiElements.Rect("Top border", _root, _width, 1, 0, height / 2), border);
        UiElements.Fill(UiElements.Rect("Bottom border", _root, _width, 1, 0, -height / 2), border);
        UiElements.Fill(UiElements.Rect("Left border", _root, 1, height, -_width / 2, 0), border);
        UiElements.Fill(UiElements.Rect("Right border", _root, 1, height, _width / 2, 0), border);
        _ui.Label(_root, "Trader", _trader, 20, _width - 190, 30, -75, height / 2 - 22).color = new Color(.843f, .851f, .851f);
        var history = Button(
            _root,
            _showHistory ? "Hide history" : "Show history",
            145,
            _width / 2 - 88,
            height / 2 - 22,
            () =>
            {
                _showHistory = !_showHistory;
                Render();
            },
            28
        );
        history.interactable = _history.Length > 0;
        history.gameObject.SetActive(_history.Length > 0);
        UiElements.Fill(UiElements.Rect("Header separator", _root, _width - 24, 1, 0, height / 2 - 44), border);
        if (messageHeight > 0)
        {
            var textScroll = Scroll(_root, "Dialogue scroll", _width - 24, textArea, 0, height / 2 - 48 - textArea / 2);
            var message = _ui.Label(textScroll.content, "Dialogue", value, 18, _width - 54, messageHeight);
            message.alignment = TextAnchor.UpperLeft;
            message.color = new Color(.843f, .851f, .851f);
            message.gameObject.AddComponent<LayoutElement>().preferredHeight = messageHeight;
        }
        var scroll = Scroll(_root, "Replies scroll", _width - 24, replyArea, 0, height / 2 - 48 - textArea - replyArea / 2);
        var content = scroll.content;
        content.GetComponent<VerticalLayoutGroup>().spacing = 3;
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
            Button(_root, "Skip playback", 145, -_width / 2 + 88, -height / 2 + 22, _skip, 28);
        Button(_root, "Leave  [ESC]", 145, _width / 2 - 88, -height / 2 + 22, _close, 28);
    }

    private StoryVisitButton Button(
        Transform parent,
        string text,
        float width,
        float x,
        float y,
        Action action,
        float height,
        bool reply = false
    ) => StoryVisitButton.CreateAction(parent, _ui.Font, text, width, x, y, action, height, _sound, reply);

    private ScrollRect Scroll(Transform parent, string name, float width, float height, float x, float y)
    {
        var scroll = _ui.Scroll(parent, name, width, height, x, y);
        scroll.GetComponent<Image>().color = Color.clear;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalScrollbar.GetComponent<Image>().color = new Color(.12f, .14f, .15f, .6f);
        scroll.verticalScrollbar.targetGraphic.color = new Color(.55f, .58f, .58f, .8f);
        return scroll;
    }

    private float Measure(string value, float width, bool reply = false)
    {
        var measure = _ui.Label(_root, "Measure", value, 18, width, 0);
        measure.fontStyle = reply ? FontStyle.Italic : FontStyle.Normal;
        var height = Math.Max(33, measure.preferredHeight + (reply ? 13 : 12));
        UiElements.Destroy(measure.gameObject);
        return height;
    }

    private float RowHeight(string text) => Measure(text, _width - 97, true);

    private void Reply(Transform parent, string text, Action action, bool enabled)
    {
        var height = RowHeight(text);
        var button = Button(parent, text, _width - 54, 0, 0, action, height, true);
        button.interactable = enabled;
        button.GetComponent<LayoutElement>().preferredHeight = height;
        var label = button.GetComponentInChildren<Text>();
        label.alignment = TextAnchor.MiddleLeft;
        if (_replyIcon)
            button.SetReplyMarker(_replyIcon!);
    }
}
