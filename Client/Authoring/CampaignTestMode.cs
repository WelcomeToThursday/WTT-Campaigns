using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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
    private static string _status = "";
    private float _nextHeartbeat;
    private static TextMeshProUGUI? _label;
    private static DefaultUIButton? _reset,
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
        if (_label)
        {
            _label!.text = _status;
            _label.gameObject.SetActive(Restricted && !Plugin.InRaid);
        }
        if (_reset)
            _reset!.Interactable = Active && !_busy && !Plugin.InRaid;
        if (_return)
            _return!.Interactable = !_busy && !Plugin.InRaid;
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
        _reset = Button(menu, "CampaignTestReset", "RESET TEST", 225, Reset);
        _return = Button(menu, "CampaignTestReturn", "RETURN TO EDITOR", 280, Return);
        _reset.gameObject.SetActive(Restricted);
        _return.gameObject.SetActive(Restricted);
        var existing = menu.transform.Find("CampaignTestStatus");
        if (existing)
            _label = existing.GetComponent<TextMeshProUGUI>();
        else
        {
            _label = Instantiate(menu._playerButton._headerLabel, menu.transform, false);
            _label.name = "CampaignTestStatus";
            _label.enableWordWrapping = true;
            _label.fontSize = 20;
            _label.alignment = TextAlignmentOptions.BottomLeft;
            _label.raycastTarget = false;
            var rect = _label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(45, 70);
            rect.sizeDelta = new Vector2(650, 80);
        }
        _label!.gameObject.SetActive(Restricted);
        _label.text = _status;
    }

    private static DefaultUIButton Button(MenuScreen screen, string name, string caption, float top, UnityEngine.Events.UnityAction click)
    {
        var button = screen.GetComponentsInChildren<DefaultUIButton>(true).AsValueEnumerable().FirstOrDefault(b => b.name == name);
        if (button)
            return button!;
        var source = screen._playerButton;
        var parent = source.transform.parent;
        var list = parent.GetComponent<VerticalLayoutGroup>() != null;
        button = Instantiate(source, list ? parent : screen.transform, false);
        button.name = name;
        button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(click);
        button.SetRawText(caption, list ? (int)source._headerLabel.fontSize : 24);
        button.SetIcon(null);
        if (!list)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-45, -top);
            rect.sizeDelta = new Vector2(280, 46);
        }
        return button;
    }
}
