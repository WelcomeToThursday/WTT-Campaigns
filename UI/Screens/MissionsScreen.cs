using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;

namespace WTT.Campaigns.UI.Screens;

/// <summary>Native-style mission list used by the campaign menu and kept independent of client transport.</summary>
public sealed class MissionsScreen : IDisposable
{
    private readonly UiElements _ui;
    private readonly RectTransform _stage;
    private MissionEntry[] _missions = Array.Empty<MissionEntry>();
    private RectTransform? _page;
    private bool _disposed;
    private string _message = "";
    private bool _messageError;
    private bool _retryVisible;
    private bool _busy;

    public GameObject Root { get; }
    public bool IsOpen => Root && Root.activeSelf;
    public Action? CloseRequested;
    public Action<string>? DeployRequested;
    public Action<string>? ResumeRequested;
    public Action<string>? CancelRequested;
    public Action? RetryRequested;
    public Action<InterfaceSound>? SoundRequested;

    public MissionsScreen(Transform parent, Font font)
    {
        _ui = new UiElements(font, sound => SoundRequested?.Invoke(sound));
        var root = UiElements.Rect("MissionsScreen", parent, 0, 0);
        UiElements.Stretch(root);
        Root = root.gameObject;
        UiElements.Fill(root, new Color(.012f, .023f, .025f, 1), true);
        _stage = UiElements.Rect("MissionsSafeArea", root, 1920, 1080);
        Root.SetActive(false);
    }

    public void Open()
    {
        if (_disposed)
            return;
        Root.SetActive(true);
        Fit();
        Render();
    }

    public void Close()
    {
        if (Root)
            Root.SetActive(false);
    }

    public void SetState(MissionEntry[] missions)
    {
        if (_disposed)
            return;
        _missions = missions ?? Array.Empty<MissionEntry>();
        _message = "";
        _messageError = false;
        _retryVisible = false;
        Render();
    }

    public void SetBusy(bool busy, string message = "")
    {
        _busy = busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _message = message;
            _messageError = false;
        }
        Render();
    }

    public void ShowMessage(string message, bool error, bool retry)
    {
        _message = message ?? "";
        _messageError = error;
        _retryVisible = retry;
        _busy = false;
        _missions = Array.Empty<MissionEntry>();
        Render();
    }

    public void SetStatus(string message, bool error = false)
    {
        if (_disposed)
            return;
        _message = message ?? "";
        _messageError = error;
        Render();
    }

    public void Fit()
    {
        if (!Root)
            return;
        var rect = ((RectTransform)Root.transform).rect;
        if (rect.width <= 0 || rect.height <= 0)
            return;
        _stage.localScale = Vector3.one * Mathf.Min(rect.width / 1920, rect.height / 1080);
    }

    private void Render()
    {
        if (_disposed || !Root)
            return;
        if (_page)
        {
            _page!.gameObject.SetActive(false);
            UiElements.Destroy(_page.gameObject);
        }

        _page = UiElements.Rect("MissionsPage", _stage, 1920, 1080);
        UiElements.Fill(_page, new Color(.025f, .033f, .031f, .98f), true);
        Header(_page);
        if (_busy)
        {
            var loadingHost = Box(_page, "MissionLoading", 650, 450, 620, 80);
            var loading = _ui.Label(loadingHost, "Text", _message.Length > 0 ? _message : "Loading missions…", 24, 620, 80);
            loading.alignment = TextAnchor.MiddleCenter;
            loading.color = UiElements.Muted;
            Button(_page, "BACK", 1655, 55, 155, 42, () => CloseRequested?.Invoke(), false);
            return;
        }

        if (_message.Length > 0 && _missions.Length == 0)
        {
            var messageHost = Box(_page, "MissionMessage", 600, 430, 720, 120);
            var message = _ui.Label(messageHost, "Text", _message, 24, 720, 120);
            message.alignment = TextAnchor.MiddleCenter;
            message.color = _messageError ? UiElements.Negative : UiElements.Muted;
            Button(_page, "BACK", 1655, 55, 155, 42, () => CloseRequested?.Invoke(), false);
            if (_retryVisible && RetryRequested != null)
                Button(_page, "RETRY", 960, 55, 155, 42, () => RetryRequested?.Invoke());
            return;
        }

        Button(_page, "BACK", 1655, 55, 155, 42, () => CloseRequested?.Invoke(), false);
        var list = _ui.Scroll(_page, "MissionList", 1420, 780, 250, -35);
        list.content.GetComponent<VerticalLayoutGroup>()!.spacing = 12;
        if (_missions.Length == 0)
        {
            var empty = _ui.Label(list.content, "MissionEmpty", "No authored missions are available for this campaign.", 22, 1300, 80);
            empty.alignment = TextAnchor.MiddleCenter;
            empty.color = UiElements.Muted;
            return;
        }

        foreach (var mission in _missions)
            RenderMission(list.content, mission);
        if (_message.Length > 0)
        {
            var statusHost = Box(_page, "MissionStatus", 300, 940, 1320, 40);
            var status = _ui.Label(statusHost, "Text", _message, 18, 1320, 40);
            status.color = _messageError ? UiElements.Negative : UiElements.Positive;
        }
    }

    private void Header(Transform parent)
    {
        var title = _ui.Label(Box(parent, "MissionTitle", 105, 52, 700, 60), "Text", "MISSIONS", 37, 700, 60);
        title.color = UiElements.Ink;
        var subtitle = _ui.Label(
            Box(parent, "MissionSubtitle", 105, 104, 1040, 38),
            "Text",
            "Complete authored routes and extract to advance your campaign.",
            18,
            1040,
            38
        );
        subtitle.color = UiElements.Muted;
    }

    private void RenderMission(Transform parent, MissionEntry mission)
    {
        const float width = 1370;
        const float height = 176;
        var row = Box(parent, "Mission " + mission.Id, 0, 0, width, height);
        UiElements.Fill(row, mission.Unlocked ? new Color(.085f, .10f, .09f, .98f) : new Color(.055f, .06f, .058f, .98f));

        var title = _ui.Label(row, "Name", mission.Name, 25, 730, 34, -width / 2 + 26, height / 2 - 30);
        title.alignment = TextAnchor.MiddleLeft;
        title.color = mission.Unlocked ? UiElements.Ink : UiElements.Muted;
        var location = _ui.Label(row, "Location", mission.Location.Length > 0 ? mission.Location : "Unknown map", 16, 500, 28, -width / 2 + 28, height / 2 - 62);
        location.color = UiElements.Muted;
        var briefing = _ui.Label(row, "Briefing", mission.Briefing.Length > 0 ? mission.Briefing : "No briefing supplied.", 17, 770, 50, -width / 2 + 28, height / 2 - 115);
        briefing.color = new Color(.68f, .69f, .64f);

        var objectives = mission.Objectives.Length == 0 ? "Route: complete all checkpoints, then extract" : string.Join("  ·  ", mission.Objectives);
        var objectiveText = _ui.Label(row, "Objectives", objectives, 16, 770, 30, -width / 2 + 28, -height / 2 + 28);
        objectiveText.color = mission.Completed ? UiElements.Positive : UiElements.Muted;

        var status = mission.Completed ? "COMPLETED" : mission.Unlocked ? (mission.Active ? "IN PROGRESS" : mission.Status.ToUpperInvariant()) : "LOCKED";
        var statusLabel = _ui.Label(row, "Status", status, 16, 300, 30, width / 2 - 405, height / 2 - 34);
        statusLabel.alignment = TextAnchor.MiddleRight;
        statusLabel.color = mission.Completed ? UiElements.Positive : mission.Unlocked ? UiElements.Accent : UiElements.Muted;
        if (mission.FailureReason.Length > 0)
        {
            var failure = _ui.Label(row, "Failure", mission.FailureReason, 14, 300, 36, width / 2 - 405, -height / 2 + 45);
            failure.alignment = TextAnchor.MiddleRight;
            failure.color = UiElements.Negative;
        }

        if (mission.CanResume)
        {
            var buttonHost = Box(row, "Resume", width / 2 - 300, -height / 2 + 18, 125, 48);
            var button = _ui.Button(buttonHost, "RESUME", 125, 0, 0, () => ResumeRequested?.Invoke(mission.Id), 44);
            button.interactable = !_busy;
        }
        if (mission.CanCancel)
        {
            var buttonHost = Box(row, "Cancel", width / 2 - 160, -height / 2 + 18, 125, 48);
            var button = _ui.Button(buttonHost, "CANCEL", 125, 0, 0, () => CancelRequested?.Invoke(mission.Id), 44);
            button.interactable = !_busy;
        }
        if (!mission.CanResume && (mission.CanDeploy || mission.CanReplay))
        {
            var caption = mission.Completed ? "REPLAY" : "DEPLOY";
            var buttonHost = Box(row, "Deploy", width / 2 - 190, -height / 2 + 18, 155, 48);
            var button = _ui.Button(buttonHost, caption, 155, 0, 0, () => DeployRequested?.Invoke(mission.Id), 44);
            button.interactable = !_busy;
        }
    }

    private Button Button(Transform parent, string text, float x, float y, float width, float height, Action action, bool fill = true)
    {
        var host = Box(parent, text, x, y, width, height);
        var button = _ui.Button(host, text, width, 0, 0, action, height);
        if (!fill && button.targetGraphic is Image image)
            image.color = Color.clear;
        return button;
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
        if (_disposed)
            return;
        _disposed = true;
        if (_page)
            UiElements.Destroy(_page!.gameObject);
        if (Root)
            UiElements.Destroy(Root);
    }
}
