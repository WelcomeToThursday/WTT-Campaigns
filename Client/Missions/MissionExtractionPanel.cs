using Comfort.Common;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Lease the native extraction presentation as a persistent mission panel.</summary>
internal sealed class MissionExtractionPanel : IDisposable
{
    private readonly ExtractionTimersPanel _panel;
    private readonly GameObject _parking;
    private readonly List<(Transform Row, Transform Parent, int Index)> _nativeRows = new();
    private readonly List<ExitTimerPanel> _rows = new();
    private readonly string _originalTitle;
    private readonly bool _originalRichText;
    private readonly bool _originalTimerEnabled,
        _originalTextEnabled,
        _originalListActive;
    private readonly string _originalClockText;
    private readonly Color _originalClockColor;
    private readonly Vector2 _originalDescriptionPosition,
        _originalListPosition,
        _originalListPivot;
    private readonly List<(CanvasGroup Group, float Alpha, bool Ignore)> _visibility = new();
    private readonly MissionInfinitySymbol _infinity;
    private readonly List<(Image Image, Color Color)> _bannerColors = new();
    private IReadOnlyList<MissionPresentation.Row> _content = Array.Empty<MissionPresentation.Row>();
    private string _status = "";
    private string _title = "";
    private bool _disposed;
    private long _shownSecond = long.MinValue;

    internal MissionExtractionPanel()
    {
        if (!MonoBehaviourSingleton<GameUI>.Instantiated || !MonoBehaviourSingleton<GameUI>.Instance.TimerPanel)
            throw new InvalidOperationException("The native mission objective panel is unavailable.");
        _panel = MonoBehaviourSingleton<GameUI>.Instance.TimerPanel;
        _originalTitle = _panel._mainTimerPanel._currentState.text;
        _originalRichText = _panel._mainTimerPanel._currentState.richText;
        var main = _panel._mainTimerPanel;
        _originalTimerEnabled = main.enabled;
        _originalTextEnabled = main.TimerText.enabled;
        _originalClockText = main.TimerText.text;
        _originalClockColor = main.TimerText.color;
        _originalListActive = _panel._timersPanel.gameObject.activeSelf;
        _originalDescriptionPosition = _panel._mainDescription.anchoredPosition;
        _originalListPosition = _panel._timersPanel.anchoredPosition;
        _originalListPivot = _panel._timersPanel.pivot;
        foreach (var group in new[] { _panel.GetComponent<CanvasGroup>(), main._canvasGroup })
            if (group)
                _visibility.Add((group, group.alpha, group.ignoreParentGroups));
        var symbol = new GameObject("MissionInfiniteTime", typeof(RectTransform), typeof(CanvasRenderer), typeof(MissionInfinitySymbol));
        var rect = (RectTransform)symbol.transform;
        rect.SetParent(main.TimerText.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _infinity = symbol.GetComponent<MissionInfinitySymbol>();
        _infinity.raycastTarget = false;
        // Native level46 extraction header: Description/Image is its solid green
        // background; Icon and State are separate children and keep their artwork.
        var background = _panel._mainDescription.Find("Image")?.GetComponent<Image>();
        if (background)
            _bannerColors.Add((background, background.color));
        _parking = new GameObject("MissionHiddenExtractRows", typeof(RectTransform));
        _parking.transform.SetParent(_panel.transform, false);
        _parking.SetActive(false);
        // Editor HUD suppression runs at canvas render time. Apply the mission
        // lease after it, rather than relying on a native five-second animation.
        Canvas.willRenderCanvases += Tick;
        Tick();
    }

    internal void Tick()
    {
        if (_disposed || !_panel)
            return;
        _panel.StopAllCoroutines();
        _panel._timersPanel.gameObject.SetActive(true);
        _panel._timersPanel.pivot = Vector2.one;
        _panel._timersPanel.anchoredPosition = Vector2.zero;
        _panel._mainDescription.anchoredPosition = new Vector2(-Mathf.Max(0, _panel._container.rect.width - 143), 0);
        foreach (var entry in _visibility)
            if (entry.Group)
            {
                entry.Group.alpha = 1;
                entry.Group.ignoreParentGroups = true;
            }
        var main = _panel._mainTimerPanel;
        main.enabled = false;
        if (MissionRaidTimer.Current is { } missionTimer)
        {
            var seconds = missionTimer.Remaining;
            var second = seconds.HasValue ? (long)Math.Ceiling(seconds.Value) : -1;
            if (_shownSecond != second)
            {
                _shownSecond = second;
                main.TimerText.text = MissionCountdown.Text(seconds);
            }
            main.TimerText.enabled = seconds.HasValue;
            main.TimerText.color = seconds is < 600 ? main._warningColor : _panel._timerPanelTemplate._defaultTimerColor;
            _infinity.gameObject.SetActive(!seconds.HasValue);
            _infinity.color = _panel._timerPanelTemplate._defaultTimerColor;
        }
        else
            _infinity.gameObject.SetActive(false);
        // Native ShowTimer/SwitchTimers may reactivate rows. Keeping them under an
        // inactive parent preserves their subscriptions without displaying ordinary exits.
        for (var i = _panel._container.childCount - 1; i >= 0; i--)
        {
            var child = _panel._container.GetChild(i);
            var timer = child.GetComponent<TimerPanel>();
            if (
                !timer
                || timer == _panel._timerPanelTemplate
                || timer is MainTimerPanel
                || (timer is ExitTimerPanel exit && _rows.Contains(exit))
            )
                continue;
            if (timer is not (ExitTimerPanel or TransitTimerPanel))
                continue;
            _nativeRows.Add((child, child.parent, child.GetSiblingIndex()));
            child.SetParent(_parking.transform, false);
        }
        _panel._mainTimerPanel._currentState.richText = false;
        _panel._mainTimerPanel._currentState.text = _title;
        foreach (var entry in _bannerColors)
            if (entry.Image)
                entry.Image.color = new Color(0.9f, 0.7f, 0.15f, entry.Color.a);
    }

    internal void Set(string title, IReadOnlyList<MissionPresentation.Row> rows, bool reveal)
    {
        if (_disposed || !_panel)
            return;
        _title = title;
        _content = rows;
        _status = "";
        while (_rows.Count < rows.Count)
        {
            var row = UnityEngine.Object.Instantiate(_panel._timerPanelTemplate, _panel._container);
            // These are presentation rows, never extraction points or running timers.
            row.enabled = false;
            row._itemsObject.SetActive(false);
            if (row._discountStats)
                row._discountStats.SetActive(false);
            if (row._requirementImage)
                row._requirementImage.gameObject.SetActive(false);
            if (row._bufferZoneTimerIcon)
                row._bufferZoneTimerIcon.gameObject.SetActive(false);
            row._pointName.richText = false;
            row._pointName.enableWordWrapping = true;
            row._pointName.overflowMode = TextOverflowModes.Ellipsis;
            var nameLayout = row._pointName.GetComponent<LayoutElement>() ?? row._pointName.gameObject.AddComponent<LayoutElement>();
            nameLayout.minWidth = nameLayout.preferredWidth = 320;
            row._timerText.richText = false;
            row._timerText.enableAutoSizing = true;
            row._timerText.fontSizeMin = 14;
            row._timerText.fontSizeMax = row._timerText.fontSize;
            _rows.Add(row);
        }
        for (var i = 0; i < _rows.Count; i++)
        {
            var view = _rows[i];
            view.gameObject.SetActive(i < rows.Count);
            if (i >= rows.Count)
                continue;
            var row = rows[i];
            view._pointStatusLabel.text = row.Label;
            view._pointName.text = row.Text;
            view._timerText.gameObject.SetActive(true);
            view._timerText.text = row.Progress;
            var color =
                row.Status == "Failed" ? new Color32(230, 92, 78, 255)
                : row.Status == "Pending" ? new Color32(145, 150, 150, 255)
                : new Color32(182, 199, 204, 255);
            view._pointStatusLabel.color = view._pointName.color = view._timerText.color = color;
            var height = Mathf.Max(50, view._pointName.GetPreferredValues(row.Text, 320, 0).y + 16);
            // The native description has a fixed 50px preferred height. Expand
            // that inner row too, so wrapped objective names receive the space.
            var description = view._pointName.transform.parent.GetComponent<LayoutElement>();
            description.minHeight = description.preferredHeight = height;
            var layout = view.GetComponent<LayoutElement>() ?? view.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
            ((RectTransform)view.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        Tick();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_panel._container);
    }

    internal void Status(string status)
    {
        if (_disposed || _status == status)
            return;
        var rows = new List<MissionPresentation.Row>(_content);
        rows.Add(
            new MissionPresentation.Row
            {
                Label = "INFO",
                Text = status,
                Status = "Pending",
            }
        );
        var content = _content;
        Set("Complete the mission", rows, true);
        _content = content;
        _status = status;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Canvas.willRenderCanvases -= Tick;
        foreach (var row in _rows)
            if (row)
                UnityEngine.Object.Destroy(row.gameObject);
        // Rows were parked back-to-front, so restore front-to-back.
        for (var i = _nativeRows.Count - 1; i >= 0; i--)
        {
            var row = _nativeRows[i];
            if (!row.Row || !row.Parent)
                continue;
            row.Row.SetParent(row.Parent, false);
            row.Row.SetSiblingIndex(row.Index);
        }
        if (_panel && _panel._mainTimerPanel)
        {
            _panel.StopAllCoroutines();
            AccessTools.Field(typeof(ExtractionTimersPanel), "_panelCoroutine")?.SetValue(_panel, null);
            AccessTools.Field(typeof(ExtractionTimersPanel), "_timerCoroutine")?.SetValue(_panel, null);
            _panel._mainTimerPanel._currentState.text = _originalTitle;
            _panel._mainTimerPanel._currentState.richText = _originalRichText;
            _panel._mainTimerPanel.enabled = _originalTimerEnabled;
            _panel._mainTimerPanel.TimerText.enabled = _originalTextEnabled;
            _panel._mainTimerPanel.TimerText.text = _originalClockText;
            _panel._mainTimerPanel.TimerText.color = _originalClockColor;
            _panel._mainDescription.anchoredPosition = _originalDescriptionPosition;
            _panel._timersPanel.anchoredPosition = _originalListPosition;
            _panel._timersPanel.pivot = _originalListPivot;
            _panel._timersPanel.gameObject.SetActive(_originalListActive);
        }
        foreach (var entry in _visibility)
            if (entry.Group)
            {
                entry.Group.alpha = entry.Alpha;
                entry.Group.ignoreParentGroups = entry.Ignore;
            }
        if (_infinity)
            UnityEngine.Object.Destroy(_infinity.gameObject);
        if (_parking)
            UnityEngine.Object.Destroy(_parking);
        foreach (var entry in _bannerColors)
            if (entry.Image)
                entry.Image.color = entry.Color;
    }
}
