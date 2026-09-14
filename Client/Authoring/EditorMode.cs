using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Bots;
using EFT.UI;
using EFT.UI.Screens;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using WTT.Campaigns.Shared.Authoring;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed class EditorMode : MonoBehaviour
{
    public enum StartupMode
    {
        Normal,
        Editor,
    }

    internal static EditorMode Instance = null!;
    internal static bool Active => Instance && Instance._requested;
    internal static bool Ready => Authenticated && !Instance._mapLoading;
    private static bool Authenticated =>
        Active
        && !Instance._connectionFailed
        && Instance._session?.SessionId.Length > 0
        && Plugin.App?.Session?.Profile?.Id == Instance._session.ProfileId;
    internal static bool Returning => Instance && Instance._returning;
    internal static bool LoadingMap => Authenticated && Instance._mapLoading && !Returning;
    internal static bool MapLoadActive => Instance && Instance._mapLoading && !Returning;
    internal static bool MapReady => Active && Authenticated && Instance._mapReady && !Instance._mapLoading && !Returning;
    internal static bool Unloading;
    internal static string SessionId => Instance?._session?.SessionId ?? "";
    internal static string ScratchProfileId => Instance?._session?.ProfileId ?? "";
    internal static string DraftId => Instance?._session?.DraftId ?? "";
    private ConfigEntry<StartupMode> _startup = null!;
    private EditorSessionResponse? _session;
    private EditorHomeScreen? _home;
    private MenuScreen? _pendingMenu;
    private bool _openingHome;
    private int _homeAfterFrame;
    private bool _requested,
        _busy,
        _hadMap,
        _mapLoading,
        _mapReady,
        _returning;
    private Task? _mapLoadCleanup;
    private string _status = "",
        _map = "";
    private int _mapIndex;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private bool _heartbeatInFlight,
        _connectionFailed;
    internal static string SelectedLayout => Instance?._session?.LayoutId ?? "";
    private float _nextHeartbeat;
    private readonly EditorHud _hud = new();
    private bool _hudError;
    private ConfigEntry<bool> _memoryDiagnostics = null!;

    private void Awake()
    {
        Instance = this;
        _startup = Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Startup mode",
            StartupMode.Normal,
            "Open the restricted editor workspace after launcher authentication."
        );
        _requested = _startup.Value == StartupMode.Editor;
        _memoryDiagnostics = Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Memory diagnostics",
            false,
            "Record read-only memory and frame timings for the first three minutes in an editor map, every five seconds."
        );
        EditorRestrictions.Enable();
        EditorDeployment.Enable();
        // Install before map loading can JIT/in-line the native culling methods.
        EditorSceneVisibility.Enable();
        EditorMemory.Enable();
        EditorRenderGuard.Enable();
        Canvas.willRenderCanvases += UpdateHud;
    }

    internal static bool PrepareBackend()
    {
        if (!Active || Returning)
            return false;
        if (Instance._session == null)
        {
            var result = JsonConvert.DeserializeObject<EditorSessionResponse>(
                RequestHandler.PostJson("/wtt-campaigns/editor/begin", JsonConvert.SerializeObject(new EditorSessionRequest()))
            );
            if (
                result?.Error != null
                || result == null
                || result.Version != 2
                || string.IsNullOrEmpty(result.ProfileId)
                || string.IsNullOrEmpty(result.SessionId)
            )
                throw new InvalidOperationException(result?.Error ?? "Editor server did not respond.");
            Instance._session = result;
        }
        Instance._connectionFailed = false;
        Plugin.SessionId = Instance._session.ProfileId;
        Plugin.Accept(
            new Profiles.ClientSnapshot
            {
                ProtocolVersion = 2,
                ActiveMode = "editor",
                EffectiveProfileId = Plugin.SessionId,
            }
        );
        return true;
    }

    internal async void Enter()
    {
        if (_busy || Plugin.InRaid)
            return;
        _busy = true;
        try
        {
            await Plugin.FlushPendingOperations();
            _requested = true;
            await Call("begin");
            PrepareBackend();
            await Plugin.App!.RecreateBackend(Plugin.App.Session.SessionMode, force: true);
            if (!Authenticated)
                throw new InvalidOperationException("Editor character did not finish loading. Retry or return to game.");
            _status = "Choose a draft and map to begin.";
        }
        catch (Exception e)
        {
            _status = e.Message;
            Plugin.Error(e);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task Call(string operation, string? draft = null, string? map = null, string layout = "")
    {
        await _requests.WaitAsync();
        try
        {
            var response = JsonConvert.DeserializeObject<EditorSessionResponse>(
                await RequestHandler.PostJsonAsync(
                    "/wtt-campaigns/editor/" + operation,
                    JsonConvert.SerializeObject(
                        new EditorSessionRequest
                        {
                            SessionId = SessionId,
                            DraftId = draft ?? DraftId,
                            Location = map ?? _map,
                            LayoutId = layout,
                        }
                    )
                )
            );
            if (response == null || response.Version != 2 || response.Error != null)
                throw new InvalidOperationException(response?.Error ?? "Editor session response is invalid.");
            _session = response;
        }
        finally
        {
            _requests.Release();
        }
    }

    private async void Run(Func<Task> action)
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            await action();
        }
        catch (Exception e)
        {
            _status = e.Message;
            Plugin.Error(e);
            // Native map/backend transitions can close home before failing.
            // Restore its controller so recovery remains reachable.
            if (Active && !Plugin.InRaid && _home && !_home!.IsOpen)
            {
                try
                {
                    await new EditorHomeScreen.Controller().ShowScreenAsync(EScreenState.Root);
                }
                catch (Exception recoveryError)
                {
                    Plugin.Error(recoveryError);
                }
            }
        }
        finally
        {
            _busy = false;
        }
    }

    internal void MenuReady(MenuScreen menu)
    {
        _pendingMenu = menu;
        _homeAfterFrame = Time.frameCount + 1;
        // Let the native Show finish registering its input before switching controllers.
        menu.gameObject.SetActive(false);
    }

    private async void ShowHome(MenuScreen menu)
    {
        _openingHome = true;
        try
        {
            if (!_home)
                BuildHome(menu);
            if (!await new EditorHomeScreen.Controller().ShowScreenAsync(EScreenState.Root))
                throw new InvalidOperationException("The Campaign editor screen could not open.");
        }
        catch (Exception e)
        {
            _status = e.Message;
            Plugin.Error(e);
            ItemUiContext.Instance.ShowMessageWindow(
                "Unable to open Campaign Editor.\n" + e.Message,
                () => Run(ReturnToGame),
                null,
                "RETURN TO GAME",
                0f,
                forceShow: true
            );
        }
        finally
        {
            _openingHome = false;
        }
    }

    private void BuildHome(MenuScreen menu)
    {
        _home = EditorHomeScreen.Create(menu);
        _home.Button(
            "EditorDraft",
            () =>
                _home.Choose(
                    "CAMPAIGN DRAFT",
                    _session!.Drafts.AsValueEnumerable().Select(d => (d.Id, d.Name)).ToArray(),
                    DraftId,
                    id =>
                        Run(async () =>
                        {
                            await Call("select", id);
                            _status = "Draft selected. Choose a layout or location.";
                        })
                )
        );
        _home.Button(
            "EditorLayout",
            () =>
            {
                var choices = new List<(string Id, string Name)> { ("", "New layout in a map") };
                choices.AddRange(_session!.Layouts.AsValueEnumerable().Select(l => (l.Id, l.Name)).ToArray());
                _home.Choose(
                    "MISSION LAYOUT",
                    choices,
                    SelectedLayout,
                    id =>
                        Run(async () =>
                        {
                            await Call("select", DraftId, layout: id);
                            var choice = _session!.Layouts.Find(l => l.Id == id);
                            if (choice != null)
                                _map = choice.Location;
                            _status = "";
                        })
                );
            }
        );
        _home.Button(
            "EditorMap",
            () =>
            {
                var maps = Plugin
                    .App!.Session.LocationSettings.locations.Values.AsValueEnumerable()
                    .Where(l => l.Enabled && l.Id != "hideout" && !string.IsNullOrEmpty(l.Id))
                    .OrderBy(l => l.Id, StringComparer.Ordinal)
                    .Select(l => (l.Id, MapName(l.Id)))
                    .ToArray();
                _home.Choose(
                    "LOCATION",
                    maps,
                    _map,
                    id =>
                    {
                        _map = id;
                        _status = "";
                    }
                );
            }
        );
        _home.Button("EditorOpen", () => Run(OpenMap));
        _home.Button(
            "EditorRefresh",
            () =>
                Run(async () =>
                {
                    await Call("status");
                    _status = "Drafts refreshed.";
                })
        );
        _home.Button(
            "EditorRetry",
            () =>
                Run(async () =>
                {
                    await Call("begin");
                    PrepareBackend();
                    await Plugin.App!.RecreateBackend(Plugin.App.Session.SessionMode, force: true);
                    if (!Authenticated)
                        throw new InvalidOperationException("Editor character did not finish loading. Retry or return to game.");
                    _status = "Editor connected.";
                })
        );
        _home.Button("EditorReturn", () => Run(ReturnToGame));
        _home.Button(
            "EditorStartup",
            () => _startup.Value = _startup.Value == StartupMode.Normal ? StartupMode.Editor : StartupMode.Normal
        );
        _home.Button("EditorWeb", () => Application.OpenURL(RequestHandler.Host.TrimEnd('/') + "/wtt-campaigns/creator"));
    }

    private static string MapName(string id) => Plugin.Localized(id + " Name", id);

    private void RefreshHome()
    {
        if (!_home || !_home!.IsOpen)
            return;
        _home.Fit();
        if (_map.Length == 0)
            SelectMap();
        var draft = _session?.Drafts.AsValueEnumerable().FirstOrDefault(d => d.Id == DraftId);
        var layout = _session?.Layouts.AsValueEnumerable().FirstOrDefault(l => l.Id == SelectedLayout);
        if (layout != null)
            _map = layout.Location;
        var hasDrafts = _session?.Drafts.Count > 0;
        var hasDraft = draft != null;
        var available = Ready && !_busy && !_returning;
        _home.Caption("EditorDraft", draft?.Name ?? (hasDrafts ? "SELECT DRAFT  ›" : "NO DRAFTS AVAILABLE"));
        _home.Text("EditorSelection", draft?.Name ?? "Select a campaign draft to begin");
        _home.Caption("EditorLayout", (layout?.Name ?? "New layout in a map") + "  ›");
        _home.Caption("EditorMap", (_map.Length == 0 ? "SELECT LOCATION" : MapName(_map)) + (layout == null ? "  ›" : ""));
        _home.Text(
            "EditorMapHelp",
            layout == null ? "Choose a location for a new layout." : "This location is set by the selected mission layout."
        );
        _home.Caption("EditorStartup", "STARTUP: " + _startup.Value.ToString().ToUpperInvariant());
        var status =
            _busy ? (_mapLoading ? "Loading " + MapName(_map) + "…" : "Working…")
            : _status.Length > 0 ? _status
            : !Ready ? "Connecting to editor…"
            : !hasDrafts ? "No drafts yet. Open Creator to create a campaign draft, then refresh."
            : !hasDraft ? "Select a campaign draft."
            : "Ready to open your map workspace.";
        _home.Text("EditorHomeStatus", status);
        _home.Interactable("EditorDraft", available && hasDrafts);
        _home.Interactable("EditorLayout", available && hasDraft);
        _home.Interactable("EditorMap", available && hasDraft && layout == null);
        _home.Interactable("EditorOpen", available && hasDraft && _map.Length > 0);
        _home.Interactable("EditorRefresh", available);
        _home.Interactable("EditorReturn", !_busy && !_returning);
        _home.Interactable("EditorRetry", !_busy && !_returning);
        _home.Visible("EditorRetry", _connectionFailed || (!Authenticated && !_busy));
        _home.Visible("EditorOpen", !_connectionFailed && (Authenticated || _busy));
        if (_busy || !Ready)
            _home.DismissPicker();
        if (!_busy && !_returning && Input.GetKeyDown(KeyCode.Escape) && !_home.DismissPicker())
            Run(ReturnToGame);
    }

    private void SelectMap()
    {
        var maps = Plugin
            .App?.Session?.LocationSettings?.locations.Values.AsValueEnumerable()
            .Where(l => l.Enabled && l.Id != "hideout" && !string.IsNullOrEmpty(l.Id))
            .OrderBy(l => l.Id, StringComparer.Ordinal)
            .ToArray();
        if (maps?.Length > 0)
        {
            _mapIndex %= maps.Length;
            _map = maps[_mapIndex].Id;
        }
    }

    private async Task OpenMap()
    {
        if (!Ready || Plugin.InRaid)
            return;
        _mapReady = false;
        _mapLoadCleanup = null;
        _mapLoading = true;
        _status = "Loading " + _map + "…";
        try
        {
            if (DraftId.Length == 0 && _session!.Drafts.Count > 0)
                await Call("select", _session.Drafts[0].Id);
            await Call("map");
            var app = Plugin.App!;
            var location = app.Session.LocationSettings.locations.Values.AsValueEnumerable().Single(l => l.Id == _map);
            app.CurrentRaidSettings.SelectedLocation = location;
            app.CurrentRaidSettings.RaidMode = ERaidMode.Local;
            app.CurrentRaidSettings.BotSettings = new BotControllerSettings(false, EBotAmount.NoBots);
            app.CurrentRaidSettings.Side = ESideType.Pmc;
            app.CurrentRaidSettings.IsPveOffline = false;
            // Editor entry bypasses StartSearchingForGame, which normally starts
            // the loading screen's elapsed clock. Reset it for every map load.
            app.Matchmaker.MatchingStartTime = DateTimeExtensions.Now;
            await app.LocalGameMatching(new TimeAndWeatherSettings(false, false, 0, 0, 0, 0, (int)ETimeFlowType.x0, 12));
            if (!Plugin.InRaid)
                throw new InvalidOperationException("Map loading did not create an editor world.");
            _mapReady = true;
        }
        catch
        {
            await RecoverFailedMapLoad();
            throw;
        }
        finally
        {
            _mapLoading = false;
        }
    }

    internal static Task RecoverFailedMapLoad() => Instance ? Instance.RecoverFailedMapLoadCore() : Task.CompletedTask;

    private Task RecoverFailedMapLoadCore()
    {
        _mapReady = false;
        return _mapLoadCleanup ??= RecoverFailedMapLoadCoreAsync();
    }

    private async Task RecoverFailedMapLoadCoreAsync()
    {
        try
        {
            await Call("unload");
        }
        catch (Exception e)
        {
            _connectionFailed = true;
            _status = e.Message;
            // The native load error remains the task's failure. Report failed
            // editor cleanup once without replacing that useful exception.
            Plugin.Error(e);
        }
    }

    internal void UnloadMap() =>
        Run(async () =>
        {
            _mapReady = false;
            RaidEditor.Instance?.EndWalkthrough();
            if (Singleton<AbstractGame>.Instance is LocalGame game)
            {
                Unloading = true;
                try
                {
                    game.Stop(Plugin.Player!.Profile.Id, ExitStatus.Survived, "Campaign editor", 0);
                }
                finally
                {
                    Unloading = false;
                }
            }
            await Task.CompletedTask;
        });

    private async Task ReturnToGame()
    {
        if (Plugin.InRaid)
            throw new InvalidOperationException("Unload the map first.");
        var returnProfile = _session?.ReturnProfileId;
        _mapReady = false;
        if (_session != null)
        {
            await Call("unload");
            await Call("end");
        }
        _returning = true;
        _session = null;
        Plugin.SessionId = returnProfile;
        try
        {
            var snapshot = await Plugin.Request("snapshot");
            Plugin.SessionId = snapshot.EffectiveProfileId;
            Plugin.Accept(snapshot);
            await Plugin.App!.RecreateBackend(Plugin.App.Session.SessionMode, force: true);
            if (Plugin.App.Session.Profile?.Id != snapshot.EffectiveProfileId)
                throw new InvalidOperationException("The previous character did not finish loading. Retry or return again.");
            _requested = false;
            if (_home)
                Destroy(_home!.gameObject);
            _home = null;
        }
        catch
        {
            _requested = true;
            throw;
        }
        finally
        {
            _returning = false;
        }
    }

    internal async Task CompleteMapExit(TarkovApplication app)
    {
        _mapReady = false;
        _mapLoading = true;
        _hadMap = false;
        try
        {
            RaidEditor.Instance?.EndWalkthrough();
            await Call("unload");
            await app.ComebackToMainMenu();
            _status = "Map unloaded. Draft remains connected.";
        }
        catch (Exception e)
        {
            _connectionFailed = true;
            _status = e.Message;
            throw;
        }
        finally
        {
            _mapLoading = false;
        }
    }

    private void Update()
    {
        EditorDiagnostics.Enabled = _memoryDiagnostics.Value;
        EditorMemory.Tick(Active && Plugin.InRaid);
        EditorDiagnostics.Tick(Active && Plugin.InRaid);
        if (!Active || !Plugin.InRaid)
            RestoreHud();
        if (!Active)
            return;
        try
        {
            if (Plugin.InRaid)
            {
                if (_connectionFailed && !_busy && !_mapLoading)
                    UnloadMap();
                _hadMap = _mapReady;
            }
            else
            {
                if (_hadMap && !_busy)
                {
                    _hadMap = false;
                    Run(async () => await Call("unload"));
                }
                if (_pendingMenu && Time.frameCount >= _homeAfterFrame && !_openingHome && !_mapLoading && !_returning)
                {
                    var menu = _pendingMenu!;
                    _pendingMenu = null;
                    ShowHome(menu);
                }
                RefreshHome();
            }
            if (
                Active
                && !_returning
                && !_connectionFailed
                && _session != null
                && !_heartbeatInFlight
                && Time.realtimeSinceStartup >= _nextHeartbeat
            )
            {
                _nextHeartbeat = Time.realtimeSinceStartup + 10;
                Heartbeat();
            }
        }
        catch (Exception e)
        {
            _status = e.Message;
            Plugin.Error(e);
        }
    }

    private async void Heartbeat()
    {
        _heartbeatInFlight = true;
        try
        {
            await Call("status");
        }
        catch (Exception e)
        {
            _connectionFailed = true;
            _status = e.Message;
            RaidEditor.Instance?.EndWalkthrough();
        }
        finally
        {
            _heartbeatInFlight = false;
        }
    }

    private void UpdateHud()
    {
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.Hud);
        try
        {
            if (!Active || !Plugin.InRaid || RaidEditor.AiPlaytestActive)
            {
                RestoreHud();
                return;
            }
            if (MonoBehaviourSingleton<GameUI>.Instantiated)
                _hud.Suppress(MonoBehaviourSingleton<GameUI>.Instance);
            // Quick slots and stance/stamina belong to CommonUI, not GameUI.
            if (MonoBehaviourSingleton<CommonUI>.Instantiated)
                _hud.Suppress(MonoBehaviourSingleton<CommonUI>.Instance.EftBattleUIScreen);
            _hudError = false;
        }
        catch (Exception e)
        {
            // Presentation must never interrupt session heartbeats or map maintenance.
            if (!_hudError)
                Plugin.Error(e);
            _hudError = true;
        }
    }

    private void RestoreHud() => _hud.Dispose();

    private void OnDestroy()
    {
        EditorMemory.Tick(false);
        EditorDiagnostics.Stop();
        Canvas.willRenderCanvases -= UpdateHud;
        RestoreHud();
        if (_home)
            Destroy(_home!.gameObject);
    }
}
