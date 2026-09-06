using EFT;
using EFT.UI;
using PlayerIcons;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Creation;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.Client;

// Reuse native views without registering a screen controller or invoking CreateProfileOperation.
internal sealed class CreationIdentity : ICreationIdentity
{
    private readonly Transform _host;
    private readonly CreationDraft _draft;
    private readonly Action _complete;
    private readonly Action _back;
    private readonly UiElements _ui;
    private readonly CreateProfileOperation.PreliminaryProfileData _data = new();
    private EftAccountSideSelectionScreen? _screen;
    private Button? _next;
    private Text? _message;
    private bool _appearance;
    private bool _appearanceReady;
    private bool _busy;
    private bool _disposed;
    private Task _pending = Task.CompletedTask;

    internal CreationIdentity(
        Transform host,
        CreationDraft draft,
        Font font,
        Action complete,
        Action back
    )
    {
        _host = host;
        _draft = draft;
        _ui = new UiElements(font, SeasonUi.Instance.PlayInterfaceSound);
        _complete = complete;
        _back = back;
        _pending = Initialize();
    }

    private async Task Initialize()
    {
        _busy = true;
        try
        {
            _message = _ui.Label(
                _host,
                "IdentityStatus",
                "LOADING CHARACTER PREVIEWS...",
                20,
                1500,
                44,
                0,
                -385
            );
            _message.alignment = TextAnchor.MiddleCenter;
            var source = Resources
                .FindObjectsOfTypeAll<EftAccountSideSelectionScreen>()
                .FirstOrDefault(value => value.gameObject.scene.IsValid());
            if (!source)
            {
                throw new InvalidOperationException(
                    "EFT's faction and appearance screen is unavailable."
                );
            }
            var profiles = await Task.WhenAll(
                CreateProfileOperation.LoadProfile(CreateProfileOperation.DEFAULT_BEAR_PROFILE),
                CreateProfileOperation.LoadProfile(CreateProfileOperation.DEFAULT_USEC_PROFILE)
            );
            if (_disposed)
            {
                return;
            }
            if (profiles.Any(profile => profile == null))
            {
                throw new InvalidOperationException(
                    "EFT's default character previews could not be loaded."
                );
            }
            _screen = UnityEngine.Object.Instantiate(source, _host, false);
            _screen.name = "SeasonalNativeIdentity";
            _screen.enabled = false;
            UiElements.Stretch((RectTransform)_screen.transform);
            _screen.gameObject.SetActive(true);
            _screen._canvasGroup.alpha = 1;
            _screen._canvasGroup.interactable = true;
            _screen._canvasGroup.blocksRaycasts = true;
            _screen._nextButton.gameObject.SetActive(false);
            _screen._backButton.gameObject.SetActive(false);
            var side = _screen._sideSelectionState;
            var head = _screen._headSelectionState;
            side.StateCanvasGroup.gameObject.SetActive(false);
            // Run native OnEnable initialization (including localized nickname fields)
            // before binding the cloned view, without exposing it on the faction step.
            SetStateVisibility(head, false);
            _data.Nickname = _draft.Nickname;
            if (Enum.TryParse<EPlayerSide>(_draft.Side, out var selectedSide))
            {
                _data.Side = selectedSide;
            }
            var previews = new Dictionary<EPlayerSide, Profile>
            {
                [EPlayerSide.Bear] = profiles[0].Clone(),
                [EPlayerSide.Usec] = profiles[1].Clone(),
            };
            if (_data.HasSide && !string.IsNullOrEmpty(_draft.HeadId))
            {
                previews[_data.Side].Customization[EBodyModelPart.Head] = _draft.HeadId;
            }
            var materials = new PlayerIconCreatorMaterialChanger(_screen._materialSettings);
            side.OnStateReady += SideReady;
            head.OnNicknameValueChanged += NicknameChanged;
            head.OnNicknameSubmited += NicknameSubmitted;
            head._preview.PlayerModelView.LoadingCompletedEvent += AppearanceLoaded;
            side.Init(_data, previews, materials);
            head.Init(_data, previews, materials, _draft.Nickname);
            head._nicknameField._inputField.text = _draft.Nickname;
            head._nicknameField._inputField.interactable = true;
            _next = NativeButton("NEXT", -440, Next);
            NativeButton("BACK", -490, Back);
            _message.transform.SetAsLastSibling();
            await side.ShowState();
            if (_disposed)
            {
                return;
            }
            if (_draft.Appearance && _data.HasSide)
            {
                await ShowAppearance();
            }
            _message.text = "";
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            if (!_disposed && _message)
            {
                _message!.text = exception.Message;
                _message.color = UiElements.Negative;
                if (!_next)
                {
                    NativeButton("BACK", -490, Back);
                }
            }
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private Button NativeButton(string caption, float y, Action action)
    {
        var button = _ui.Button(_host, caption, 240, 0, y, action, 48);
        button.targetGraphic.color = Color.clear;
        button.GetComponentInChildren<Text>().fontSize = 28;
        return button;
    }

    private void SideReady(bool ready) => Refresh();

    private void NicknameChanged(string value)
    {
        _data.Nickname = value;
        Refresh();
    }

    private async void NicknameSubmitted(string value)
    {
        _data.Nickname = value;
        // Native submission disables the input after raising its event.
        await Task.Yield();
        if (!_disposed && _screen)
        {
            var head = _screen!._headSelectionState;
            head.NicknameError(head._nicknameField.ValidationError(value));
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_disposed || !_next)
        {
            return;
        }
        _next!.interactable =
            !_busy
            && (
                _appearance
                    ? _appearanceReady
                        && _screen!._headSelectionState._nicknameField.ValidationError(
                            _data.Nickname
                        ) == ENicknameError.ValidNickname
                    : _data.HasSide
            );
        _next.GetComponentInChildren<Text>().color = _next.interactable
            ? UiElements.Ink
            : UiElements.Muted;
    }

    private async Task ShowAppearance()
    {
        await _screen!._sideSelectionState.HideState();
        if (_disposed)
        {
            return;
        }
        _appearance = true;
        _appearanceReady = false;
        var head = _screen._headSelectionState;
        SetStateVisibility(head, false);
        if (_draft.Side == _data.Side.ToString() && !string.IsNullOrEmpty(_draft.HeadId))
        {
            _screen._headSelectionState._previewProfiles[_data.Side].Customization[
                EBodyModelPart.Head
            ] = _draft.HeadId;
        }
        await _screen._headSelectionState.ShowState();
        if (_disposed)
        {
            return;
        }
        SetStateVisibility(head, true);
        var voice = head._voiceTemplates.FindIndex(value => value.Key.ToString() == _draft.VoiceId);
        if (voice >= 0)
        {
            head._selectedVoiceIndex = voice;
            head._voiceSelector.UpdateValue(voice, false);
            _data.VoiceId = _draft.VoiceId;
        }
        AppearanceLoaded();
        Plugin.LogInfo(
            $"Seasonal appearance: active={head.StateCanvasGroup.gameObject.activeInHierarchy}, alpha={head.StateCanvasGroup.alpha}, heads={head._faceCards.Count}, voices={head._voiceTemplates.Count}, modelReady={head._preview.PlayerModelView.LoadingComplete}"
        );
    }

    private static void SetStateVisibility(EftScreenState state, bool visible)
    {
        var group = state.StateCanvasGroup;
        group.gameObject.SetActive(true);
        group.alpha = visible ? 1 : 0;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }

    private void AppearanceLoaded()
    {
        if (_disposed || !_screen || !_appearance)
        {
            return;
        }
        var head = _screen!._headSelectionState;
        _appearanceReady =
            head.StateCanvasGroup.gameObject.activeInHierarchy
            && head.StateCanvasGroup.alpha > .99f
            && head._faceCards.Count > 0
            && head._voiceTemplates.Count > 0
            && head._preview.PlayerModelView.LoadingComplete;
        Refresh();
    }

    private void Next()
    {
        if (_busy || _disposed || !_next || !_next!.interactable)
        {
            return;
        }
        if (_appearance)
        {
            SaveDraft();
            _complete();
        }
        else
        {
            _pending = Transition(true);
        }
    }

    public void Back()
    {
        if (_busy || _disposed)
        {
            return;
        }
        if (_appearance)
        {
            SaveDraft();
            _pending = Transition(false);
        }
        else
        {
            _draft.Appearance = false;
            _back();
        }
    }

    private async Task Transition(bool appearance)
    {
        _busy = true;
        Refresh();
        try
        {
            if (appearance)
            {
                await ShowAppearance();
            }
            else
            {
                await _screen!._headSelectionState.HideState();
                if (!_disposed)
                {
                    _appearance = false;
                    _appearanceReady = false;
                    await _screen._sideSelectionState.ShowState();
                    SetStateVisibility(_screen._sideSelectionState, true);
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            if (!_disposed && _message)
            {
                _message!.text = exception.Message;
            }
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void SaveDraft()
    {
        _draft.Side = _data.Side.ToString();
        _draft.Nickname = _data.Nickname;
        _draft.HeadId = _data.HeadId;
        var head = _screen!._headSelectionState;
        // Read the selected dropdown directly; voice playback completes asynchronously.
        var voice = head._voiceSelector.CurrentIndex;
        _draft.VoiceId =
            voice >= 0 && voice < head._voiceTemplates.Count
                ? head._voiceTemplates[voice].Key.ToString()
                : _data.VoiceId;
        _draft.Appearance = _appearance;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_screen)
        {
            // Let native async model loads finish against an inactive, detached host before cleanup.
            _screen!.transform.SetParent(null, false);
            _screen.gameObject.SetActive(false);
        }
        Release();
    }

    private async void Release()
    {
        try
        {
            await _pending;
            if (!_screen)
            {
                return;
            }
            _screen!._sideSelectionState.OnStateReady -= SideReady;
            _screen._headSelectionState.OnNicknameValueChanged -= NicknameChanged;
            _screen._headSelectionState.OnNicknameSubmited -= NicknameSubmitted;
            _screen._headSelectionState._preview.PlayerModelView.LoadingCompletedEvent -=
                AppearanceLoaded;
            _screen._sideSelectionState.Close();
            _screen._headSelectionState.Close();
            foreach (var preview in _screen.GetComponentsInChildren<PlayerProfilePreview>(true))
            {
                preview.Close();
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        finally
        {
            if (_screen)
            {
                UnityEngine.Object.Destroy(_screen!.gameObject);
            }
        }
    }
}
