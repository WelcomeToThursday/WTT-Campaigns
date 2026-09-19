using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;

namespace WTT.Campaigns.UI.Screens;

public sealed class MapLayersScreen : IDisposable
{
    private const float ContentLeft = 104;
    private const float ContentWidth = 1712;
    private readonly UiElements _ui;
    private readonly RectTransform _stage;
    private RectTransform? _page;
    private MapLayerEntry[] _layers = Array.Empty<MapLayerEntry>();
    private readonly MapLayerEntry[] _samples = MapLayerSamples.Create();
    private bool _showSamples;
    private string _message = "";
    private bool _busy,
        _error;
    private float _scroll = 1;
    private ScrollRect? _list;
    public GameObject Root { get; }
    public Action? CloseRequested,
        RefreshRequested;
    public Action<string, bool>? ToggleRequested;
    public Action<InterfaceSound>? SoundRequested;

    public MapLayersScreen(Transform parent, Font font)
    {
        _ui = new UiElements(font, sound => SoundRequested?.Invoke(sound));
        var root = UiElements.Rect("MapLayersScreen", parent, 0, 0);
        UiElements.Stretch(root);
        Root = root.gameObject;
        UiElements.Fill(root, new Color(.025f, .033f, .031f, 1), true);
        _stage = UiElements.Rect("MapLayersSafeArea", root, 1920, 1080);
        Root.SetActive(false);
    }

    public void Open()
    {
        Root.SetActive(true);
        Fit();
        Render();
    }

    public void Close() => Root.SetActive(false);

    public void SetState(MapLayerEntry[] layers, string message = "")
    {
        _layers = layers;
        _busy = _error = false;
        _message = message;
        Render();
    }

    public void Status(string message, bool busy, bool error = false)
    {
        _message = message;
        _busy = busy;
        _error = error;
        Render();
    }

    public void Fit()
    {
        var rect = ((RectTransform)Root.transform).rect;
        if (rect.width > 0 && rect.height > 0)
            _stage.localScale = Vector3.one * Mathf.Min(rect.width / 1920, rect.height / 1080);
    }

    private void Render(bool resetScroll = false)
    {
        if (_page)
        {
            if (_list && !resetScroll)
                _scroll = _list!.verticalNormalizedPosition;
            _page!.gameObject.SetActive(false);
            UiElements.Destroy(_page.gameObject);
        }
        if (resetScroll)
            _scroll = 1;
        var sampleView = _showSamples;
        var displayed = sampleView ? _samples : _layers;
        _page = UiElements.Rect("MapLayersPage", _stage, 1920, 1080);
        _ui.Label(Box(_page, "Title", ContentLeft, 52, 900, 60), "Text", "MAP LAYERS", 37, 900, 60);
        var subtitle = _ui.Label(
            Box(_page, "Subtitle", ContentLeft, 122, ContentWidth, 52),
            "Text",
            _showSamples
                ? "TEST ENTRIES · Check map groups, scrolling and switches. These samples do not change raids or saved selections."
                : "Choose map edits from published campaigns for this regular character. Changes are saved for your next raid.",
            20,
            ContentWidth,
            52
        );
        subtitle.color = UiElements.Muted;
        var samples = _ui.Button(
            Box(_page, "TestEntries", ContentLeft + ContentWidth - 596, 60, 232, 44),
            _showSamples ? "SHOW REAL LAYERS" : "SHOW TEST ENTRIES",
            232,
            0,
            0,
            () =>
            {
                _showSamples = !_showSamples;
                Render(resetScroll: true);
            }
        );
        samples.interactable = !_busy;
        var back = _ui.Button(
            Box(_page, "Back", ContentLeft + ContentWidth - 170, 60, 170, 44),
            "BACK",
            170,
            0,
            0,
            () => CloseRequested?.Invoke()
        );
        back.interactable = !_busy;
        var refresh = _ui.Button(
            Box(_page, "Refresh", ContentLeft + ContentWidth - 352, 60, 170, 44),
            "REFRESH",
            170,
            0,
            0,
            () => RefreshRequested?.Invoke()
        );
        refresh.interactable = !_busy;
        var listHost = Box(_page, "ListRegion", ContentLeft, 204, ContentWidth, 720);
        _list = _ui.Scroll(listHost, "MapLayerList", ContentWidth, 720, 0, 0);
        var layout = _list.content.GetComponent<VerticalLayoutGroup>()!;
        layout.spacing = 12;
        layout.padding = new RectOffset(20, 20, 12, 20);
        _list.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        foreach (var map in displayed.GroupBy(l => l.Location).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var group = UiElements.Rect("Map " + map.Key, _list.content, 0, 64);
            group.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            var heading = _ui.Label(group, "MapName", map.Key, 25, 0, 0);
            UiElements.Stretch(heading.rectTransform, 24, 270);
            heading.color = UiElements.Accent;
            var count = _ui.Label(group, "EnabledCount", map.Count(l => l.Enabled) + " / " + map.Count() + " enabled", 18, 0, 0);
            UiElements.Stretch(count.rectTransform, 24, 24);
            count.alignment = TextAnchor.MiddleRight;
            count.color = UiElements.Muted;
            foreach (var layer in map.OrderBy(l => l.Campaign).ThenBy(l => l.Name))
            {
                var row = UiElements.Rect("Layer " + layer.Key, _list.content, 0, 112);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 112;
                UiElements.Fill(row, new Color(.07f, .085f, .075f, .98f));
                var name = _ui.Label(row, "Name", layer.Name, 23, 0, 0);
                UiElements.Stretch(name.rectTransform, 24, 236, 17, 58);
                name.alignment = TextAnchor.MiddleLeft;
                var source = _ui.Label(row, "Campaign", layer.Campaign, 17, 0, 0);
                UiElements.Stretch(source.rectTransform, 24, 236, 61, 19);
                source.color = UiElements.Muted;
                var button = _ui.Button(
                    row,
                    layer.Enabled ? "ENABLED" : "DISABLED",
                    180,
                    0,
                    0,
                    () =>
                    {
                        if (sampleView)
                        {
                            layer.Enabled = !layer.Enabled;
                            Render();
                        }
                        else
                            ToggleRequested?.Invoke(layer.Key, !layer.Enabled);
                    },
                    46
                );
                var buttonRect = (RectTransform)button.transform;
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1, .5f);
                buttonRect.pivot = new Vector2(1, .5f);
                buttonRect.anchoredPosition = new Vector2(-24, 0);
                button.interactable = !_busy;
                if (button.targetGraphic is Image fill && layer.Enabled)
                    fill.color = new Color(.19f, .26f, .13f, 1);
            }
        }
        if (displayed.Length == 0)
        {
            var empty = _ui.Label(
                _list.viewport,
                "Empty",
                _busy
                    ? "Loading map layers…"
                    : "No published map layers are available.\nUse SHOW TEST ENTRIES to preview map groups and switches.",
                23,
                0,
                0
            );
            UiElements.Stretch(empty.rectTransform, 40, 40, 40, 40);
            empty.alignment = TextAnchor.MiddleCenter;
            empty.color = UiElements.Muted;
        }
        var status = _ui.Label(
            Box(_page, "Status", ContentLeft, 952, ContentWidth, 72),
            "Text",
            _showSamples ? "UI test entries only. Switch changes are temporary and are never sent to the server." : _message,
            18,
            ContentWidth,
            72
        );
        status.alignment = TextAnchor.UpperLeft;
        status.color = _error && !_showSamples ? UiElements.Negative : UiElements.Muted;
        Canvas.ForceUpdateCanvases();
        _list.verticalNormalizedPosition = _scroll;
    }

    private static RectTransform Box(Transform parent, string name, float x, float y, float width, float height)
    {
        var rect = UiElements.Rect(name, parent, width, height);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x + width / 2, -y - height / 2);
        return rect;
    }

    public void Dispose()
    {
        if (Root)
            UiElements.Destroy(Root);
    }
}
