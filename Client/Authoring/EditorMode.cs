using BepInEx.Configuration;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.UI;
using EFT.UI.Screens;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Rendering;
using WTT.Campaigns.Client.Authoring.Views;
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
    private EditorContentMode _homeTab = EditorContentMode.Mission;
    private bool _newLevel;
    private readonly Dictionary<EditorContentMode, (string Draft, string Layout)> _homeSelections = new();
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
    internal static WTT.Campaigns.Shared.Authoring.EditorContentMode SelectedContentMode => Instance?._session?.Mode ?? WTT.Campaigns.Shared.Authoring.EditorContentMode.None;
    internal static string EditorTitle => WTT.Campaigns.Shared.Authoring.EditorContentRules.Title(Instance?._session?.Mode ?? WTT.Campaigns.Shared.Authoring.EditorContentMode.None);
    internal static string SelectedLayout => Instance?._session?.LayoutId ?? "";
    private float _nextHeartbeat;
    private readonly EditorHud _hud = new();
    private bool _hudError;
    private string? _startupError;
    private ConfigEntry<bool> _memoryDiagnostics = null!;

    private void Awake()
    {
        Instance = this;
        _startup = Plugin.Instance.Config.Bind(
            "editor",
            "Startup mode",
            StartupMode.Normal,
            "Open the restricted editor workspace after launcher authentication."
        );
        _requested = _startup.Value == StartupMode.Editor;
        _memoryDiagnostics = Plugin.Instance.Config.Bind(
            "editor",
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

    internal static bool PrepareBackendForStartup(bool initialBackend) =>
        EditorStartupRecovery.Prepare(
            initialBackend,
            PrepareBackend,
            error =>
            {
                // Keep the normal backend reachable for native raid recovery. Never
                // clear the server's raid guard or change the saved startup preference.
                Instance._requested = false;
                Instance._connectionFailed = true;
                Instance._startupError = "Editor could not open.\n" + error.Message;
                Plugin.Error(error);
            }
        );

    internal void NormalMenuReady(MenuScreen menu)
    {
        if (_startupError == null)
            return;
        _pendingMenu = menu;
        _homeAfterFrame = Time.frameCount + 1;
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
        RestoreBackendMapState();
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

    private static void RestoreBackendMapState()
    {
        // A reconnected backend can reuse a server session whose old client
        // disappeared in a map. Only clear that map lease when no local map exists.
        if (Plugin.InRaid || MapLoadActive || Instance._session!.Location.Length == 0)
            return;
        var current = Instance._session;
        var restored = JsonConvert.DeserializeObject<EditorSessionResponse>(
            RequestHandler.PostJson(
                "/wtt-campaigns/editor/unload",
                JsonConvert.SerializeObject(new EditorSessionRequest { SessionId = current.SessionId })
            )
        );
        if (
            restored == null
            || restored.Error != null
            || restored.Version != 2
            || restored.SessionId != current.SessionId
            || restored.ProfileId != current.ProfileId
            || restored.Location.Length != 0
        )
            throw new InvalidOperationException(restored?.Error ?? "Editor session could not return to home. Retry connection.");
        Instance._session = restored;
    }

    internal async void Enter()
    {
        if (_busy || Plugin.InRaid || CampaignTestMode.Restricted)
            return;
        _busy = true;
        try
        {
            // Leave the native button dispatch before saving or rebuilding its menu.
            await Cysharp.Threading.Tasks.UniTask.NextFrame(cancellationToken: this.GetCancellationTokenOnDestroy());
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
            if (!_requested && PreloaderUI.Instance)
                PreloaderUI.Instance.ShowErrorScreen("Editor", e.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    internal void SuspendForCampaignTest()
    {
        if (Plugin.InRaid || _mapLoading)
            throw new InvalidOperationException("Unload the editor map before testing the campaign flow.");
        _requested = false;
        _pendingMenu = null;
        RestoreHud();
        if (_home)
            _home!.Close();
    }

    internal async Task ResumeAfterCampaignTest(string draft, string layout)
    {
        _requested = true;
        _connectionFailed = false;
        _mapReady = _mapLoading = _hadMap = false;
        _session = null;
        try
        {
            await Call("begin");
            if (draft.Length > 0)
                await Call("select", draft, layout: layout);
            PrepareBackend();
            await Plugin.App!.RecreateBackend(Plugin.App.Session.SessionMode, force: true);
            if (!Authenticated)
                throw new InvalidOperationException("Editor did not finish loading. Use Retry connection to return.");
            CampaignTestMode.EditorResumed();
            _status = "Campaign test ended. Your source draft and real character are unchanged.";
        }
        catch
        {
            _connectionFailed = true;
            _status = "Test ended. Retry connection to return to the editor.";
            if (_home && !_home!.IsOpen)
            {
                try
                {
                    await new EditorHomeScreen.Controller().ShowScreenAsync(EScreenState.Root);
                }
                catch (Exception e)
                {
                    Plugin.Error(e);
                }
            }
            throw;
        }
    }

    private async Task Call(string operation, string? draft = null, string? map = null, string layout = "", string name = "")
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
                            Name = name,
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
        if (Authenticated)
            CampaignTestMode.EditorResumed();
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
                throw new InvalidOperationException("The editor screen could not open.");
        }
        catch (Exception e)
        {
            _status = e.Message;
            Plugin.Error(e);
            ItemUiContext.Instance.ShowMessageWindow(
                "Unable to open Editor.\n" + e.Message,
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
        _homeTab = _session?.Mode == EditorContentMode.Level ? EditorContentMode.Level : EditorContentMode.Mission;
        _home.Button("EditorMissionsTab", () => Run(() => SelectHomeTab(EditorContentMode.Mission)));
        _home.Button("EditorLevelsTab", () => Run(() => SelectHomeTab(EditorContentMode.Level)));
        _home.Button("EditorNewLevel", () => { _newLevel = true; _home.ResetLevelName(); _status = "Name your level and choose its map."; });
        _home.Button(
            "EditorDraft",
            () =>
            {
                if (_homeTab == EditorContentMode.Level)
                {
                    ChooseLevel();
                    return;
                }
                _home.Choose(
                    _homeTab == EditorContentMode.Mission ? "MISSION CONTENT" : "LEVEL CONTENT",
                    _session!.Drafts.AsValueEnumerable().Where(d => EditorContentRules.Includes(d, _homeTab)).Select(d => (d.Id, d.Name)).ToArray(),
                    DraftId,
                    id =>
                        Run(async () =>
                        {
                            await SelectHomeDraft(id);
                            _status = "";
                        })
                );
            }
        );
        _home.Button(
            "EditorLayout",
            () =>
            {
                var choices = new List<(string Id, string Name)>();
                if (_homeTab == EditorContentMode.Level)
                    choices.Add(("", "Create a new level layout"));
                choices.AddRange(_session!.Layouts.AsValueEnumerable().Where(l => l.Mode == _homeTab).Select(l => (l.Id, l.Name)).ToArray());
                _home.Choose(
                    _homeTab == EditorContentMode.Mission ? "MISSION LAYOUTS" : "LEVEL LAYOUTS",
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
        _home.Button("EditorCampaignTest", () => Run(() => CampaignTestMode.Enter(SessionId, DraftId, SelectedLayout)));
        _home.Button(
            "EditorMissionTest",
            () =>
                Run(async () =>
                {
                    await OpenMap();
                    if (_session?.Mode != WTT.Campaigns.Shared.Authoring.EditorContentMode.Mission) return;
                    var response = await EditorMissionTestClient.PrepareAsync(DraftId, SelectedLayout, useEncounters: true);
                    if (!RaidEditor.Instance)
                        throw new InvalidOperationException("The map editor is not ready for mission testing.");
                    RaidEditor.Instance.StartEditorMissionTest(response);
                })
        );
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
        _home.Button("EditorWeb", () => Application.OpenURL(RequestHandler.Host.TrimEnd('/') + (_homeTab == EditorContentMode.Level ? "/wtt-campaigns/creator/levels" + (!_newLevel && SelectedLayout.Length > 0 ? "?draft=" + Uri.EscapeDataString(DraftId) + "&layout=" + Uri.EscapeDataString(SelectedLayout) : "") : "/wtt-campaigns/creator/missions")));
    }

    private static string MapName(string id) => Plugin.Localized(id + " Name", id);

    private void ChooseLevel()
    {
        var levels = _session!.Levels;
        var choices = levels.AsValueEnumerable().Select(l =>
            (l.DraftId + "/" + l.Id, l.Name + " · " + MapName(l.Location) +
                (levels.FindAll(other => other.Name == l.Name && other.Location == l.Location).Count > 1 ? " · " + l.DraftId.Substring(Math.Max(0, l.DraftId.Length - 6)) + "/" + l.Id.Substring(Math.Max(0, l.Id.Length - 6)) : ""))).ToArray();
        _home!.Choose("LEVEL LIBRARY", choices, _newLevel ? "" : DraftId + "/" + SelectedLayout, key => Run(async () =>
        {
            var selected = levels.Find(l => l.DraftId + "/" + l.Id == key)!;
            await Call("select", selected.DraftId, layout: selected.Id);
            _newLevel = false;
            _map = selected.Location;
            _status = "";
        }));
    }

    private async Task SelectHomeTab(EditorContentMode mode)
    {
        if (mode == _homeTab) return;
        if (_session?.Mode == _homeTab)
            _homeSelections[_homeTab] = (DraftId, SelectedLayout);
        _home!.DismissPicker();
        _homeTab = mode;
        _newLevel = false;
        _status = "";
        _homeSelections.TryGetValue(mode, out var saved);
        if (mode == EditorContentMode.Level)
        {
            var level = _session!.Levels.Find(l => l.DraftId == saved.Draft && l.Id == saved.Layout)
                ?? _session.Levels.Find(l => l.DraftId == DraftId && l.Id == SelectedLayout)
                ?? _session.Levels.AsValueEnumerable().FirstOrDefault();
            if (level != null)
                await Call("select", level.DraftId, layout: level.Id);
            return;
        }
        var draft = _session!.Drafts.Find(d => d.Id == saved.Draft && EditorContentRules.Includes(d, mode))
            ?? _session.Drafts.Find(d => d.Id == DraftId && EditorContentRules.Includes(d, mode))
            ?? _session.Drafts.Find(d => EditorContentRules.Includes(d, mode));
        if (draft != null)
            await SelectHomeDraft(draft.Id, draft.Id == saved.Draft ? saved.Layout : "");
    }

    private async Task SelectHomeDraft(string id, string preferredLayout = "")
    {
        await Call("select", id);
        var layout = _session!.Layouts.Find(l => l.Id == preferredLayout && l.Mode == _homeTab)
            ?? (_homeTab == EditorContentMode.Mission ? _session.Layouts.Find(l => l.Mode == _homeTab) : null);
        if (layout != null && SelectedLayout != layout.Id)
            await Call("select", id, layout: layout.Id);
    }

    private void RefreshHome()
    {
        if (!_home || !_home!.IsOpen)
            return;
        _home.Fit();
        if (_map.Length == 0)
            SelectMap();
        var missionTab = _homeTab == EditorContentMode.Mission;
        var draft = _session?.Drafts.AsValueEnumerable().FirstOrDefault(d => d.Id == DraftId && EditorContentRules.Includes(d, _homeTab));
        var layout = draft == null || (!missionTab && _newLevel) ? null : _session?.Layouts.AsValueEnumerable().FirstOrDefault(l => l.Id == SelectedLayout && l.Mode == _homeTab);
        if (layout != null)
            _map = layout.Location;
        _home.Text("EditorTitle", "WTT / EDITOR");
        _home.SelectedTab("EditorMissionsTab", missionTab);
        _home.SelectedTab("EditorLevelsTab", !missionTab);
        _home.Text("TabHelp", missionTab ? "Authored missions with AI, checkpoints and player starts." : "Layouts for ordinary PMC raids. Native AI and player spawns.");
        _home.Text("DraftHeading", missionTab ? "MISSION CONTENT" : "LEVEL LIBRARY");
        _home.Text("DraftHelp", missionTab ? "Choose a standalone mission or campaign with mission layouts." : "Select a saved level, or create one with New Level.");
        _home.Text("MapHeading", missionTab ? "MISSION WORKSPACE" : "LEVEL WORKSPACE");
        _home.Text("LayoutHeading", missionTab ? "MISSION LAYOUT" : "LEVEL NAME");
        _home.Text("CreatorHelp", missionTab ? "Create missions, link campaigns and edit story content." : "Publish and manage your selected level in Creator. New levels start disabled.");
        _home.Visible("EditorMissionTest", missionTab);
        _home.Visible("EditorCampaignTest", missionTab && draft != null && _session?.HasStory == true);
        _home.Text("WorkspaceHelp", missionTab ? "Tests use disposable state. Your real character is preserved." : "Levels stay disabled in ordinary raids until you enable them in Creator.");
        var hasDrafts = missionTab ? _session?.Drafts.Exists(d => EditorContentRules.Includes(d, _homeTab)) == true : _session?.Levels.Count > 0;
        var hasDraft = missionTab ? draft != null : layout != null;
        var available = Ready && !_busy && !_returning;
        _home.Visible("EditorWeb", missionTab || hasDraft);
        _home.Visible("CreatorHelp", missionTab || hasDraft);
        _home.Caption("EditorDraft", (missionTab ? draft?.Name : layout?.Name) ?? (hasDrafts ? "SELECT " + (missionTab ? "CONTENT" : "LEVEL") + "  ›" : missionTab ? "NO MISSIONS AVAILABLE" : "NO LEVELS YET"));
        _home.Text("EditorSelection", (missionTab ? draft?.Name : layout?.Name) ?? (missionTab ? "Select mission content to begin" : _newLevel ? "Create a new level" : "Choose a level or select New Level"));
        _home.Visible("EditorNewLevel", !missionTab);
        _home.Visible("CreatorHeading", missionTab);
        _home.Visible("EditorLevelName", !missionTab && _newLevel);
        _home.Visible("EditorLayout", missionTab || !_newLevel);
        _home.Interactable("EditorNewLevel", available);
        _home.Caption("EditorRefresh", missionTab ? "REFRESH DRAFTS" : "REFRESH LEVELS");
        _home.Caption("EditorLayout", (layout?.Name ?? (missionTab ? "Select a mission layout" : "Create a new level layout")) + "  ›");
        _home.Caption("EditorMap", (_map.Length == 0 ? "SELECT LOCATION" : MapName(_map)) + (layout == null ? "  ›" : ""));
        _home.Text(
            "EditorMapHelp",
            layout == null ? (missionTab ? "Select a mission layout to use its location." : "Choose a location for a new level layout.") : "This location is set by the selected layout."
        );
        _home.Caption("EditorStartup", "STARTUP: " + _startup.Value.ToString().ToUpperInvariant());
        var status =
            _busy ? (_mapLoading ? "Loading " + MapName(_map) + "…" : "Working…")
            : _status.Length > 0 ? _status
            : !Ready ? "Connecting to editor…"
            : !missionTab && _newLevel ? "Enter a name and map, then create your level."
            : !hasDrafts ? (missionTab ? "No missions yet. Create a mission in Creator, then refresh." : "No levels yet. Select New Level to create one here.")
            : !hasDraft ? (missionTab ? "Select mission content." : "Select a level from the library.")
            : "Ready to open your map workspace.";
        _home.Text("EditorHomeStatus", status);
        _home.Interactable("EditorDraft", available && hasDrafts);
        _home.Interactable("EditorMissionsTab", available);
        _home.Interactable("EditorLevelsTab", available);
        _home.Interactable("EditorLayout", available && hasDraft && missionTab);
        _home.Interactable("EditorMap", available && _newLevel && !missionTab);
        _home.Interactable("EditorOpen", available && _map.Length > 0 && ((!missionTab && _newLevel && !string.IsNullOrWhiteSpace(_home.LevelName)) || (hasDraft && _session?.Mode == _homeTab && layout != null)));
        _home.Caption("EditorOpen", missionTab ? "OPEN MISSION EDITOR" : _newLevel ? "CREATE & OPEN LEVEL" : "OPEN LEVEL EDITOR");
        _home.Interactable("EditorCampaignTest", available && hasDraft);
        _home.Interactable("EditorMissionTest", available && hasDraft && layout != null);
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
        if (_homeTab == EditorContentMode.Level && _newLevel)
        {
            await Call("create-level", name: _home!.LevelName.Trim());
            _newLevel = false;
        }
        if (_session?.Mode != _homeTab)
            throw new InvalidOperationException("Select content from the current editor tab before opening its map.");
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
            app._raidSettings = Missions.LocalRaidLaunch.CreateSettings(app.Session.LocationSettings, location);
            // Editor entry bypasses StartSearchingForGame, which normally starts
            // the loading screen's elapsed clock. Reset it for every map load.
            app.Matchmaker.MatchingStartTime = DateTimeExtensions.Now;
            // Release the Toolkit home before native loading changes scenes/input.
            if (_home)
                _home!.Close();
            Plugin.LogInfo("Editor loading: entering native map load for " + _map);
            using (UI.NativeLoadingStatus.Begin("Opening " + EditorTitle + "…"))
                await app.LocalGameMatching(app.CurrentRaidSettings.TimeAndWeatherSettings);
            Plugin.LogInfo("Editor loading: native map load completed");
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
                    game.Stop(Plugin.Player!.Profile.Id, ExitStatus.Survived, "editor", 0);
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
        if (!Active && _startupError != null && _pendingMenu && Time.frameCount >= _homeAfterFrame && !Plugin.InRaid)
        {
            var message = _startupError;
            _startupError = null;
            _pendingMenu = null;
            ItemUiContext.Instance.ShowMessageWindow(message, null, null, "OK", 0f, forceShow: true);
        }
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
