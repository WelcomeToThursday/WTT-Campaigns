using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Small non-interactive objective panel shown only while a mission run is active.</summary>
internal sealed class MissionHud : IDisposable
{
    private readonly GameObject _canvas;
    private readonly Text _title;
    private readonly Text _objective;
    private readonly Text _status;
    private bool _disposed;

    internal MissionHud(string missionName)
    {
        _canvas = new GameObject("MissionHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        UnityEngine.Object.DontDestroyOnLoad(_canvas);
        var canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 28500;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1800, 980);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var font = ResolveFont();
        var panel = UiElements.Rect("MissionObjectivePanel", _canvas.transform, 470, 156, 26, 26);
        panel.anchorMin = new Vector2(0, 1);
        panel.anchorMax = new Vector2(0, 1);
        panel.pivot = new Vector2(0, 1);
        panel.anchoredPosition = new Vector2(26, -26);
        UiElements.Fill(panel, new Color(.025f, .03f, .027f, .84f));
        var ui = new UiElements(font);
        _title = ui.Label(panel, "MissionTitle", missionName, 20, 430, 32, 0, 44);
        _title.color = UiElements.Ink;
        _objective = ui.Label(panel, "MissionObjective", "Preparing route…", 17, 430, 42, 0, 2);
        _objective.color = UiElements.Accent;
        _status = ui.Label(panel, "MissionStatus", "", 15, 430, 34, 0, -42);
        _status.color = UiElements.Muted;
        SetRoute(0, 0, false, "Preparing mission…");
    }

    internal void SetRoute(int completed, int total, bool exitReached, string status)
    {
        if (_disposed)
            return;
        _objective.text = exitReached
            ? "Exit reached · completing mission"
            : completed >= total
                ? "All checkpoints complete · reach the authored exit"
                : "Checkpoint " + Math.Min(completed + 1, total) + " of " + total;
        _status.text = status ?? "";
    }

    internal void SetStatus(string status)
    {
        if (!_disposed)
            _status.text = status ?? "";
    }

    private static Font ResolveFont()
    {
        try
        {
            var bundled = SeasonUi.Instance?.UiBundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf");
            if (bundled)
                return bundled;
        }
        catch
        {
            // The fallback keeps the in-raid HUD available if an optional UI asset is absent.
        }
        return Resources.FindObjectsOfTypeAll<Font>().AsValueEnumerable().FirstOrDefault(font => font.name.Equals("Jovanny Lemonad - Bender", StringComparison.OrdinalIgnoreCase))
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_canvas)
            UnityEngine.Object.Destroy(_canvas);
    }
}
