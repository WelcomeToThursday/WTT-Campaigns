using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Shared.Authoring;

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

    internal static async Task Enter(string editorSession, string draft, string layout)
    {
        if (Restricted || _busy || Plugin.InRaid || !EditorMode.Ready)
            throw new InvalidOperationException("Return to editor home before starting a campaign test.");
        _busy = true;
        _editorSession = editorSession;
        _draft = draft;
        _layout = layout;
        try
        {
            await Plugin.FlushPendingOperations();
            _session = await Call("create");
            _ended = false;
            _returnFlushed = _resetFlushed = false;
            _resetTestId = null;
            EditorMode.Instance.SuspendForCampaignTest();
            await LoadCharacter();
            _status = "CAMPAIGN TEST · Accept the test quest from Prapor, then open Missions.";
        }
        catch
        {
            _status = "Campaign test connection failed. Use Reset test or Return to editor.";
            if (_session != null)
            {
                try
                {
                    await Call("end");
                    _ended = true;
                    _snapshot = null;
                    Plugin.SessionId = _session.ReturnProfileId;
                    await EditorMode.Instance.ResumeAfterCampaignTest(_draft, _layout);
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
            throw new InvalidOperationException("The disposable campaign character did not match the test session.");
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
                    }
                )
            )
        );
        if (response == null || response.Version != 1 || response.Error != null)
            throw new InvalidOperationException(response?.Error ?? "The campaign test server did not respond.");
        if (action != "end" && (response.TestId.Length == 0 || response.ProfileId.Length == 0 || response.SeasonId.Length == 0))
            throw new InvalidOperationException("The campaign test response is incomplete.");
        return response;
    }

    private static async void Reset()
    {
        if (!Active || _busy || Plugin.InRaid)
            return;
        _busy = true;
        _status = "Resetting disposable character…";
        try
        {
            _resetTestId ??= _session!.TestId;
            if (!_resetFlushed)
            {
                await Plugin.FlushPendingOperations();
                _resetFlushed = true;
            }
            _session = await Call("reset", _resetTestId);
            _snapshot = null;
            await LoadCharacter();
            _resetFlushed = _returnFlushed = false;
            _resetTestId = null;
            _status = "CAMPAIGN TEST · Reset complete. Accept the quest from Prapor again.";
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

    private static async void Return()
    {
        if (!Restricted || _busy || Plugin.InRaid)
            return;
        _busy = true;
        _status = "Returning to editor…";
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
            await EditorMode.Instance.ResumeAfterCampaignTest(_draft, _layout);
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
            var status = await Call("status");
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
        var controls = new VisualElement();
        controls.style.position = Position.Absolute;
        controls.style.right = 45;
        controls.style.top = 225;
        controls.style.width = 280;
        _menuDocument.Content.Add(controls);
        _reset = new Button(Reset) { text = "RESET TEST" };
        _return = new Button(Return) { text = "RETURN TO EDITOR" };
        controls.Add(_reset);
        controls.Add(_return);
        _label = new Label(_status) { enableRichText = false, pickingMode = PickingMode.Ignore };
        _label.style.position = Position.Absolute;
        _label.style.left = 45;
        _label.style.bottom = 70;
        _label.style.width = 650;
        _label.style.whiteSpace = WhiteSpace.Normal;
        _menuDocument.Content.Add(_label);
        _menuDocument.SetVisible(true);
    }

    private void OnDestroy()
    {
        _menuDocument?.Dispose();
        _menuDocument = null;
    }
}
