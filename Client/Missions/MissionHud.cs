using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Story;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Native mission objectives, story notifications, and the checkpoint retry dialog.</summary>
internal sealed class MissionHud : IDisposable
{
    private readonly GameObject _canvas;
    private readonly MissionExtractionPanel _objectives;
    private readonly MissionPresentation _presentation = new();
    private readonly MissionDefinition _mission;
    private readonly MapLayout _layout;
    private MissionRun? _presentedRun;
    private long _attempt;
    private bool _disposed;
    private GameObject? _failure;
    private MissionRetryMenuInput? _failureInput;

    internal MissionHud(MissionDefinition mission, MapLayout layout, MissionRun run)
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

        _mission = mission;
        _layout = layout;
        _objectives = new MissionExtractionPanel();
        _presentation.Reset(run);
        Accept(run, reveal: true);
    }

    internal void Tick() => _objectives.Tick();

    internal void Accept(MissionRun run, bool restored = false, bool reveal = false)
    {
        if (_disposed)
            return;
        restored |= _attempt != 0 && _attempt != run.AttemptGeneration;
        if (!restored && !reveal && ReferenceEquals(_presentedRun, run))
            return;
        _presentedRun = run;
        _attempt = run.AttemptGeneration;
        if (restored)
            _presentation.Reset(run);
        var notices = _presentation.Accept(_mission, _layout, run);
        _objectives.Set("Complete the mission", MissionPresentation.Rows(_mission, _layout, run), reveal || restored || notices.Count > 0);
        foreach (var notice in notices)
        {
            try
            {
                EFT.Communications.NotificationManager.DisplayNotification(
                    new StoryChapterNotification(notice.Title, notice.Description, notice.Status, notice.Icon)
                );
            }
            catch (Exception exception)
            {
                // A presentation failure must never fail an acknowledged mission transition.
                Plugin.LogInfo("Mission notification unavailable: " + exception.Message);
            }
        }
    }

    internal void SetStatus(string status)
    {
        if (!_disposed)
            _objectives.Status(status);
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
        ui.Button(
            panel,
            "Retry checkpoint",
            245,
            -133,
            -70,
            () =>
            {
                HideFailure();
                retry?.Invoke();
            }
        ).interactable = retry != null;
        ui.Button(
            panel,
            "End attempt",
            245,
            133,
            -70,
            () =>
            {
                HideFailure();
                end();
            }
        );
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    internal void HideFailure()
    {
        _failureInput?.Release();
        _failureInput = null;
        if (_failure)
            UnityEngine.Object.Destroy(_failure);
        _failure = null;
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
        _objectives.Dispose();
        if (_canvas)
            UnityEngine.Object.Destroy(_canvas);
    }
}
