using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Shared.Authoring;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

/// <summary>Reconnects the native backend to an isolated, server-owned campaign character.</summary>
internal sealed class CampaignTestMode : MonoBehaviour
{
    private static CampaignTestResponse? _session;
    private static ClientSnapshot? _snapshot;
    private static string _editorSession = "",
        _draft = "",
        _layout = "";
    private static bool _busy,
        _heartbeat,
        _ended,
        _returnFlushed,
        _resetFlushed;
    private static string? _resetTestId;
    private static string? _operationId;
    private static string _operationAction = "";
    private static long _chosenRevision;
    private static bool _confirmReset;
    private static Button? _apply;
    private static ClientSnapshot? _returnSnapshot;
    private static string _status = "";
    private float _nextHeartbeat;
    private static EditorToolkitDocument? _menuDocument;
    private static MenuScreen? _menu;
    private static Label? _label;
    private static Button? _reset,
        _return;
    internal static bool Restricted => _session != null;
    internal static bool Active => Restricted && !_ended;

    internal static void EditorResumed()
    {
        if (!_ended)
            return;
        _session = null;
        _snapshot = null;
        _ended = false;
    }

    internal static bool PrepareBackend()
    {
        if (!Active)
            return false;
        Plugin.SessionId = _session!.ProfileId;
        if (_snapshot != null)
            Plugin.Accept(_snapshot);
        return true;
    }

    internal static async Task Enter(string editorSession, string draft, string layout, long revision = 0)
    {
        if (Restricted || _busy || Plugin.InRaid || (editorSession.Length > 0 && !EditorMode.Ready))
            throw new InvalidOperationException("Return to editor home before starting a campaign test.");
        _busy = true;
        _editorSession = editorSession;
        _draft = draft;
        _layout = layout;
        _chosenRevision = revision;
        _confirmReset = false;
        _returnSnapshot = Plugin.Current;
        try
        {
            await Plugin.FlushPendingOperations();
            if (_chosenRevision == 0)
                _chosenRevision = (await Drafts()).AsValueEnumerable().FirstOrDefault(d => d.Id == draft)?.Revision ?? 0;
            _session = await Call("resume");
            _ended = false;
            _returnFlushed = _resetFlushed = false;
            _resetTestId = _operationId = null;
            if (_editorSession.Length > 0)
                EditorMode.Instance.SuspendForCampaignTest();
            await LoadCharacter();
            _status = _session.Message;
        }
        catch
        {
            _status = "Campaign test connection failed. Use Reset test character or Return.";
            if (_session != null)
            {
                try
                {
                    await Call("end");
                    _ended = true;
                    _snapshot = null;
                    Plugin.SessionId = _session.ReturnProfileId;
                    await RestoreReturn();
                    EditorResumed();
                }
                catch (Exception recoveryError)
                {
                    Plugin.Error(recoveryError);
                }
            }
            throw;
        }
        finally
        {
            _busy = false;
        }
    }

    private static async Task LoadCharacter()
    {
        Plugin.SessionId = _session!.ProfileId;
        _snapshot = await Plugin.Request("snapshot");
        if (
            _snapshot.EffectiveProfileId != _session.ProfileId
            || _snapshot.SeasonId != _session.SeasonId
            || _snapshot.ActiveMode != "seasonal"
        )
            throw new InvalidOperationException("The campaign test character did not match the test session.");
        await Plugin.Reload(_snapshot);
    }

    private static async Task<CampaignTestResponse> Call(string action, string? testId = null)
    {
        var response = JsonConvert.DeserializeObject<CampaignTestResponse>(
            await RequestHandler.PostJsonAsync(
                "/wtt-campaigns/test-campaign/" + action,
                JsonConvert.SerializeObject(
                    new CampaignTestRequest
                    {
                        EditorSessionId = _editorSession,
                        DraftId = _draft,
                        TestId = testId ?? _session?.TestId ?? "",
                        Action = action,
                        OperationId = _operationId ?? "",
                        ExpectedDraftRevision = _session?.LatestDraftRevision ?? _chosenRevision,
                        ExpectedLoadedRevision = _session?.LoadedDraftRevision ?? 0,
                    }
                )
            )
        );
        if (response == null || response.Version != 2 || response.Error != null)
            throw new InvalidOperationException(response?.Error ?? "The campaign test server did not respond.");
        if (
            action != "end"
            && action != "list"
            && (response.TestId.Length == 0 || response.ProfileId.Length == 0 || response.SeasonId.Length == 0)
        )
            throw new InvalidOperationException("The campaign test response is incomplete.");
        return response;
    }

    internal static async Task<List<CampaignTestDraft>> Drafts() => (await Call("list")).Drafts;

    private static void Reset()
    {
        if (!_confirmReset)
        {
            _confirmReset = true;
            _status = "Reset deletes this test character’s progress. Select CONFIRM RESET, or Apply / Return to cancel.";
            _reset!.text = "CONFIRM RESET";
            return;
        }
        RefreshTest(true);
    }

    private static async void RefreshTest(bool reset)
    {
        if (!Active || _busy || Plugin.InRaid)
            return;
        _busy = true;
        _confirmReset = false;
        _reset!.text = "RESET TEST CHARACTER";
        _status = reset ? "Resetting test character…" : "Applying saved changes…";
        try
        {
            var action = reset ? "reset" : "apply";
            if (_operationAction != action)
            {
                _operationId = _resetTestId = null;
                _resetFlushed = false;
            }
            _operationAction = action;
            _operationId ??= Guid.NewGuid().ToString("N");
            _resetTestId ??= _session!.TestId;
            if (!_resetFlushed)
            {
                await Plugin.FlushPendingOperations();
                _resetFlushed = true;
            }
            _session = await Call(reset ? "reset" : "apply", _resetTestId);
            _snapshot = null;
            await LoadCharacter();
            _resetFlushed = _returnFlushed = false;
            _resetTestId = _operationId = null;
            _status = _session.Message;
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

    private static async Task RestoreReturn()
    {
        if (_editorSession.Length > 0)
            await EditorMode.Instance.ResumeAfterCampaignTest(_draft, _layout);
        else if (_returnSnapshot != null)
            await Plugin.Reload(_returnSnapshot);
    }

    private static async void Return()
    {
        if (!Restricted || _busy || Plugin.InRaid)
            return;
        _busy = true;
        _confirmReset = false;
        _status = "Saving test progress…";
        try
        {
            if (!_ended)
            {
                if (!_returnFlushed)
                {
                    await Plugin.FlushPendingOperations();
                    _returnFlushed = true;
                }
                await Call("end");
                _ended = true;
                _snapshot = null;
                Plugin.SessionId = _session!.ReturnProfileId;
            }
            await RestoreReturn();
            EditorResumed();
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

    private async void Update()
    {
        if (_menuDocument != null)
        {
            if (!_menu)
            {
                _menuDocument.Dispose();
                _menuDocument = null;
            }
            else
            {
                _menuDocument.SetVisible(Restricted && !Plugin.InRaid && _menu!.gameObject.activeInHierarchy);
                _label!.text = _status;
                _apply!.SetEnabled(
                    Active
                        && !_busy
                        && !Plugin.InRaid
                        && (
                            _session!.LatestDraftRevision != _session.LoadedDraftRevision
                            || (_operationId != null && _operationAction == "apply")
                        )
                );
                _reset!.SetEnabled(Active && !_busy && !Plugin.InRaid);
                _return!.SetEnabled(!_busy && !Plugin.InRaid);
            }
        }
        if (!Active || _busy || _heartbeat || Time.realtimeSinceStartup < _nextHeartbeat)
            return;
        _nextHeartbeat = Time.realtimeSinceStartup + 20;
        _heartbeat = true;
        try
        {
            var expected = _session;
            var status = await Call("status");
            if (!ReferenceEquals(_session, expected) || _busy)
                return;
            _session = status;
            if (!_confirmReset && status.Status != "Ended")
                _status = status.Message;
            if (status.Status == "Ended")
            {
                _ended = true;
                _snapshot = null;
                Plugin.SessionId = status.ReturnProfileId;
                _status = "Test ended. Select Return to editor to reconnect.";
            }
        }
        catch (Exception e)
        {
            _status = "Campaign test: " + e.Message;
            Plugin.Error(e);
        }
        finally
        {
            _heartbeat = false;
        }
    }

    internal static void AttachMenu(MenuScreen menu)
    {
        _menuDocument?.Dispose();
        _menuDocument = null;
        _menu = menu;
        if (!Restricted)
            return;
        _menuDocument = new EditorToolkitDocument("Campaign Test", 32100);
        var controls = _menuDocument.Clone<VisualElement>("CampaignTest");
        _menuDocument.Content.Add(controls);
        _reset = controls.Q<Button>("Reset");
        _return = controls.Q<Button>("Return");
        _apply = controls.Q<Button>("Apply");
        _apply.clicked += () => RefreshTest(false);
        _return.text = _editorSession.Length > 0 ? "RETURN TO EDITOR" : "RETURN TO CHARACTER";
        _reset.clicked += Reset;
        _return.clicked += Return;
        _label = controls.Q<Label>("Status");
        _label.text = _status;
        _menuDocument.SetVisible(true);
    }

    private void OnDestroy()
    {
        _menuDocument?.Dispose();
        _menuDocument = null;
    }
}
