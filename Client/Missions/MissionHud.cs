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
    private GameObject? _failure;
    private MissionRetryMenuInput? _failureInput;

    internal MissionHud(string missionName)
    {
        _canvas = new GameObject("MissionHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
        _objective.text =
            total <= 0 ? "Preparing route…"
            : exitReached ? "Exit reached · completing mission"
            : completed >= total ? "Route checkpoints: " + total + "/" + total + " · reach the exit"
            : "Route checkpoints: " + Math.Max(0, completed) + "/" + total + " · next: " + (Math.Max(0, completed) + 1);
        _status.text = status ?? "";
    }

    internal void SetStatus(string status)
    {
        if (!_disposed)
            _status.text = status ?? "";
    }

    internal void ShowFailure(string reason, string checkpoint, Action? retry, Action end)
    {
        HideFailure();
        var ui = new UiElements(ResolveFont());
        var panel = UiElements.Rect("MissionRetry", _canvas.transform, 600, 240);
        _failure = panel.gameObject;
        _failureInput = _failure.AddComponent<MissionRetryMenuInput>();
        UiElements.Fill(panel, new Color(.025f, .03f, .027f, .97f), true);
        ui.Label(panel, "Failure", reason, 21, 550, 70, 0, 60).alignment = TextAnchor.MiddleCenter;
        ui.Label(panel, "Checkpoint", "Retry point: " + checkpoint, 17, 550, 35, 0, 0).alignment = TextAnchor.MiddleCenter;
        ui.Button(panel, "Retry checkpoint", 245, -133, -70, () => { HideFailure(); retry?.Invoke(); }).interactable = retry != null;
        ui.Button(panel, "End attempt", 245, 133, -70, () => { HideFailure(); end(); });
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    internal void HideFailure()
    {
        _failureInput?.Release();
        _failureInput = null;
        if (_failure) UnityEngine.Object.Destroy(_failure);
        _failure = null;
    }

    internal void SetObjectives(WTT.Campaigns.Shared.Missions.MissionDefinition mission, WTT.Campaigns.Shared.Spatial.MapLayout layout,
        WTT.Campaigns.Shared.Missions.MissionLogicState state)
    {
        if (_disposed || mission.Objectives.Count == 0) return;
        var current = mission.Objectives.AsValueEnumerable().FirstOrDefault(o => state.Objectives.TryGetValue(o.Id, out var p) && p.Status == "Failed")
            ?? mission.Objectives.AsValueEnumerable().FirstOrDefault(o => state.Objectives.TryGetValue(o.Id, out var p) && p.Status == "Active")
            ?? mission.Objectives.AsValueEnumerable().FirstOrDefault(o => !state.Objectives.TryGetValue(o.Id, out var p) || p.Status == "Pending");
        if (current == null) return;
        var progress = WTT.Campaigns.Shared.Missions.MissionLogic.Progress(state, current.Id);
        _objective.text = current.Name + (current.Type == WTT.Campaigns.Shared.Missions.MissionObjective.Defend
            ? $" · {progress.Seconds:0}/{current.Seconds:0}s"
            : current.Type is WTT.Campaigns.Shared.Missions.MissionObjective.Eliminate or WTT.Campaigns.Shared.Missions.MissionObjective.Target
                ? $" · {progress.Count}/{WTT.Campaigns.Shared.Missions.MissionLogic.Expected(layout, current)}" : " · " + progress.Status);
        _status.text = progress.Status == "Pending" ? "Waiting for activation event" : progress.Detail;
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
        return Resources
                .FindObjectsOfTypeAll<Font>()
                .AsValueEnumerable()
                .FirstOrDefault(font => font.name.Equals("Jovanny Lemonad - Bender", StringComparison.OrdinalIgnoreCase))
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        HideFailure();
        if (_canvas)
            UnityEngine.Object.Destroy(_canvas);
    }
}
