using EFT;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Hub;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Screens;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Main-menu Missions screen and normal local-raid launcher.</summary>
internal sealed class MissionUi : MonoBehaviour
{
    internal static MissionUi Instance = null!;

    private GameObject? _canvas;
    private MissionsScreen? _screen;
    private bool _opening;
    private bool _destroyed;
    private int _blockedThrough = -1;
    private long _revision;
    private string _prepareMissionId = "";
    private string _prepareOperationId = "";
    private MissionResponse? _lastResponse;

    internal bool IsOpen => _screen != null && _screen.Root && _screen.Root.activeSelf;
    internal bool InputBlocked => IsOpen || Time.frameCount <= _blockedThrough;
    internal static bool Available
    {
        get
        {
            var app = Plugin.App;
            return !Authoring.EditorMode.Active
                && !Plugin.InRaid
                && !Plugin.Busy
                && Plugin.Current?.ActiveMode == "seasonal"
                && Plugin.Current.EffectiveProfileId.Length > 0
                && app?.Session?.Profile?.Id == Plugin.Current.EffectiveProfileId;
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (
            Authoring.EditorMode.Active
            || Plugin.InRaid
            || Plugin.Current?.ActiveMode != "seasonal"
            || Plugin.App?.Session?.Profile?.Id != Plugin.Current?.EffectiveProfileId
        )
        {
            if (IsOpen)
                Close();
            return;
        }
        if (IsOpen)
        {
            _screen!.Fit();
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F7))
                Close();
        }
        else if (Input.GetKeyDown(KeyCode.F7) && Available)
        {
            Open();
        }
    }

    internal async void Open()
    {
        if (!Available || _opening)
            return;
        SeasonUi.Instance.CloseForNavigation();
        SeasonHubUi.Instance.Close();
        EnsureScreen();
        _screen!.Open();
        _screen.SetBusy(true, "Loading missions…");
        _opening = true;
        try
        {
            var response = await MissionClient.ListAsync();
            if (_destroyed)
                return;
            _lastResponse = response;
            _revision = response.Revision;
            _screen.SetBusy(false);
            _screen.SetState(Presentation(response));
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _screen.SetBusy(false);
            _screen.ShowMessage(exception.Message, true, true);
        }
        finally
        {
            _opening = false;
        }
    }

    private void EnsureScreen()
    {
        if (_screen != null)
            return;
        _canvas = new GameObject("MissionsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_canvas);
        var canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 29010;
        canvas.pixelPerfect = true;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1800, 980);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var font = ResolveFont();
        _screen = new MissionsScreen(_canvas.transform, font)
        {
            CloseRequested = Close,
            DeployRequested = Deploy,
            ResumeRequested = Resume,
            CancelRequested = Cancel,
            RetryRequested = Open,
            SoundRequested = SeasonUi.Instance.PlayInterfaceSound,
        };
    }

    private static Font ResolveFont()
    {
        try
        {
            var bundled = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf");
            if (bundled)
                return bundled;
        }
        catch
        {
            // The native fallback keeps the list usable when the optional UI bundle is unavailable.
        }
        return Resources
                .FindObjectsOfTypeAll<Font>()
                .AsValueEnumerable()
                .FirstOrDefault(font => font.name.Equals("Jovanny Lemonad - Bender", StringComparison.OrdinalIgnoreCase))
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static MissionEntry[] Presentation(MissionResponse response)
    {
        return (response.Missions ?? new List<MissionSummary>())
            .AsValueEnumerable()
            .Where(summary => summary?.Definition != null)
            .Select(summary =>
            {
                var definition = summary.Definition;
                return new MissionEntry
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    Briefing = definition.Briefing,
                    Location = definition.LayoutId,
                    Status = summary.Status,
                    FailureReason = summary.FailureReason,
                    Objectives = new[] { "Complete every authored checkpoint in order", "Extract from the authored exit" },
                    Unlocked = summary.Unlocked,
                    Completed = summary.Completed,
                    Active = summary.Active,
                    CanDeploy = summary.Unlocked && !summary.Active,
                    CanReplay = summary.Completed,
                    CanResume =
                        summary.Active && response.Run?.MissionId == definition.Id && response.Run?.Status == MissionRunStatuses.Prepared,
                    CanCancel = summary.Active && response.Run?.MissionId == definition.Id,
                };
            })
            .ToArray();
    }

    private async void Deploy(string missionId)
    {
        if (!Available || _opening)
            return;
        _opening = true;
        Plugin.Busy = true;
        _screen?.SetBusy(true, "Preparing mission raid…");
        MissionResponse? prepared = null;
        try
        {
            await Plugin.FlushPendingOperations();
            if (_prepareMissionId != missionId || _prepareOperationId.Length == 0)
            {
                _prepareMissionId = missionId;
                _prepareOperationId = MissionClient.NewOperationId();
            }
            prepared = await MissionClient.PrepareAsync(missionId, _revision, _prepareOperationId);
            await LaunchPreparedAsync(prepared);
            _prepareMissionId = "";
            _prepareOperationId = "";
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            MissionRaidRuntime.ClearPending();
            if (prepared?.Run != null)
            {
                try
                {
                    await MissionClient.CancelAsync(
                        prepared.Run.MissionId,
                        prepared.Run.RunId,
                        prepared.Run.RaidId,
                        prepared.Revision,
                        operationId: MissionClient.NewOperationId(), attemptGeneration: prepared.Run.AttemptGeneration
                    );
                    _prepareMissionId = "";
                    _prepareOperationId = "";
                }
                catch (Exception cancelError)
                {
                    Plugin.Error(cancelError);
                }
            }
            MissionRaidRuntime.AbortPendingRaid("Mission launch failed");
            _screen?.Open();
            _screen?.SetBusy(false);
            if (!await ShowAuthoritativeRunAsync(missionId, exception.Message))
                _screen?.ShowMessage(exception.Message, true, true);
        }
        finally
        {
            _opening = false;
            Plugin.Busy = false;
        }
    }

    private async Task LaunchPreparedAsync(MissionResponse prepared)
    {
        if (prepared?.Descriptor == null || prepared.Run == null)
            throw new InvalidDataException("The mission server returned no deployable descriptor.");
        if (prepared.Run.Status != MissionRunStatuses.Prepared)
            throw new InvalidOperationException("This mission preparation is no longer deployable.");
        _lastResponse = prepared;
        _revision = prepared.Revision;
        var descriptor = prepared.Descriptor;
        var run = prepared.Run;
        MissionRaidRuntime.SetPending(descriptor, run, prepared.Revision);
        var app = Plugin.App ?? throw new InvalidOperationException("The game menu is not ready.");
        var location = app
            .Session.LocationSettings.locations.Values.AsValueEnumerable()
            .SingleOrDefault(l => l.Id == descriptor.Layout.Location);
        if (location == null)
            throw new InvalidOperationException("The mission map is not installed: " + descriptor.Layout.Location);
        app._raidSettings = LocalRaidLaunch.CreateSettings(app.Session.LocationSettings, location);
        app.Matchmaker.MatchingStartTime = DateTimeExtensions.Now;
        _screen?.Close();
        _blockedThrough = Time.frameCount + 1;
        using (UI.NativeLoadingStatus.Begin("Deploying campaign mission…"))
            await app.LocalGameMatching(app.CurrentRaidSettings.TimeAndWeatherSettings);
        if (!Plugin.InRaid)
            throw new InvalidOperationException("Mission loading did not create a local raid.");
    }

    private async Task<bool> ShowAuthoritativeRunAsync(string missionId, string failure)
    {
        try
        {
            var response = await MissionClient.ListAsync();
            _lastResponse = response;
            _revision = response.Revision;
            _screen?.SetState(Presentation(response));
            var run = response.Run;
            if (run == null || MissionRunStatuses.IsTerminal(run.Status))
            {
                // A terminal run cannot be recovered by replaying its prepare
                // receipt. Drop the receipt so the next deploy starts a fresh
                // operation for the next attempt.
                _prepareMissionId = "";
                _prepareOperationId = "";
            }
            else if (run.MissionId != missionId || run.Status == MissionRunStatuses.Active)
            {
                // Prepared receipts are useful for response-loss recovery. Once
                // another mission or an active run is authoritative, retaining
                // this operation would replay an obsolete prepare request.
                _prepareMissionId = "";
                _prepareOperationId = "";
            }
            if (run != null && run.MissionId == missionId && !MissionRunStatuses.IsTerminal(run.Status))
            {
                _screen?.SetStatus(
                    run.Status == MissionRunStatuses.Prepared
                        ? "Mission preparation was saved. Resume it or cancel it below."
                        : "An active mission run was found. Cancel it after returning to the menu.",
                    true
                );
            }
            else
            {
                _screen?.SetStatus("Mission launch failed: " + failure, true);
            }
            return true;
        }
        catch (Exception refreshError)
        {
            Plugin.Error(refreshError);
            return false;
        }
    }

    private async void Resume(string missionId)
    {
        if (!Available || _opening)
            return;
        _opening = true;
        Plugin.Busy = true;
        _screen?.SetBusy(true, "Resuming mission raid…");
        MissionResponse? prepared = null;
        try
        {
            var response = _lastResponse;
            if (response?.Run == null || response.Run.MissionId != missionId || response.Run.Status != MissionRunStatuses.Prepared)
                response = await MissionClient.ListAsync();
            if (response.Run == null || response.Run.MissionId != missionId || response.Run.Status != MissionRunStatuses.Prepared)
                throw new InvalidOperationException("The prepared mission run is no longer available.");
            prepared = response;
            var descriptor = await MissionClient.DescriptorAsync(missionId, response.Run.RunId, response.Run.RaidId);
            prepared = descriptor;
            await LaunchPreparedAsync(descriptor);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            MissionRaidRuntime.ClearPending();
            if (prepared?.Run != null && prepared.Run.Status == MissionRunStatuses.Prepared)
            {
                try
                {
                    var cancelled = await MissionClient.CancelAsync(
                        prepared.Run.MissionId,
                        prepared.Run.RunId,
                        prepared.Run.RaidId,
                        prepared.Revision,
                        operationId: MissionClient.NewOperationId(), attemptGeneration: prepared.Run.AttemptGeneration
                    );
                    if (cancelled.Run == null || MissionRunStatuses.IsTerminal(cancelled.Run.Status))
                    {
                        _prepareMissionId = "";
                        _prepareOperationId = "";
                    }
                }
                catch (Exception cancelError)
                {
                    Plugin.Error(cancelError);
                }
            }
            MissionRaidRuntime.AbortPendingRaid("Mission resume failed");
            _screen?.Open();
            _screen?.SetBusy(false);
            if (!await ShowAuthoritativeRunAsync(missionId, exception.Message))
                _screen?.ShowMessage(exception.Message, true, true);
        }
        finally
        {
            _opening = false;
            Plugin.Busy = false;
        }
    }

    private async void Cancel(string missionId)
    {
        if (!Available || _opening)
            return;
        _opening = true;
        Plugin.Busy = true;
        _screen?.SetBusy(true, "Cancelling mission preparation…");
        try
        {
            var response = _lastResponse;
            if (response?.Run == null || response.Run.MissionId != missionId)
                response = await MissionClient.ListAsync();
            if (response.Run == null || response.Run.MissionId != missionId || MissionRunStatuses.IsTerminal(response.Run.Status))
                throw new InvalidOperationException("The mission run is no longer available.");
            var cancelled = await MissionClient.CancelAsync(
                missionId,
                response.Run.RunId,
                response.Run.RaidId,
                response.Revision,
                operationId: MissionClient.NewOperationId(), attemptGeneration: response.Run.AttemptGeneration
            );
            _lastResponse = cancelled;
            _revision = cancelled.Revision;
            _prepareMissionId = "";
            _prepareOperationId = "";
            _screen?.SetBusy(false);
            _screen?.SetState(Presentation(cancelled));
            _screen?.SetStatus("Mission preparation cancelled.");
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _screen?.Open();
            _screen?.SetBusy(false);
            if (!await ShowAuthoritativeRunAsync(missionId, exception.Message))
                _screen?.ShowMessage(exception.Message, true, true);
        }
        finally
        {
            _opening = false;
            Plugin.Busy = false;
        }
    }

    private void Close()
    {
        if (Plugin.Busy || _opening)
            return;
        _screen?.Close();
        _blockedThrough = Time.frameCount + 1;
    }

    private void OnDestroy()
    {
        _destroyed = true;
        _screen?.Dispose();
        _screen = null;
        if (Instance == this)
            Instance = null!;
    }
}
