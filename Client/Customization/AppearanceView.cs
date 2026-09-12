using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.Customization;
using EFT.UI;
using PlayerIcons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Customization;

// The creation prefab supplies the real card renderer, model camera, faction logos and voice controls.
internal sealed class AppearanceView : IDisposable
{
    private static readonly Dictionary<HeadSelectionState, AppearanceView> Owners = new();
    private readonly HashSet<Task> _pending = new();
    private int _previewGeneration;
    private EftAccountSideSelectionScreen? _screen;
    private readonly Profile _profile;
    private readonly Transform _host;
    private bool _disposed;
    private bool _ready;
    private Task _load = Task.CompletedTask;
    internal TextMeshProUGUI? Status { get; private set; }
    internal HeadSelectionState? Head => _screen?._headSelectionState;
    internal bool Ready => _ready && !_disposed && Head!._preview.PlayerModelView.LoadingComplete;
    internal bool SelectionReady => _ready && !_disposed;

    internal static bool TryGet(HeadSelectionState head, out AppearanceView view) => Owners.TryGetValue(head, out view);

    internal AppearanceView(Transform host, Profile profile)
    {
        _host = host;
        _profile = profile;
    }

    internal Task Load() => _load = Initialize();

    private async Task Initialize()
    {
        var source =
            Resources
                .FindObjectsOfTypeAll<EftAccountSideSelectionScreen>()
                .AsValueEnumerable()
                .FirstOrDefault(value =>
                    value.gameObject.scene.IsValid() && value.name != "CampaignCustomization" && value.name != "SeasonalNativeIdentity"
                )
            ?? throw new InvalidOperationException("EFT's appearance screen is unavailable.");
        var heads = Singleton<CustomizationSolver>.Instance.GetAvailableHeads(_profile.Side).AsValueEnumerable().ToArray();
        var voices = Singleton<CustomizationSolver>.Instance.GetAvailableVoices(_profile.Side).AsValueEnumerable().ToArray();
        if (heads.Length == 0 || voices.Length == 0)
            throw new InvalidOperationException("No native appearance assets are available for this faction.");
        // Native PrepareFaceSelector assumes the current head is present in its available list.
        if (
            !heads.AsValueEnumerable().Any(head => head.Id == _profile.Customization[EBodyModelPart.Head])
            || !voices.AsValueEnumerable().Any(voice => voice.Id == _profile.Customization[EBodyModelPart.Voice])
        )
            throw new InvalidOperationException("The character's current appearance is unavailable in the installed game data.");

        _screen = UnityEngine.Object.Instantiate(source, _host, false);
        _screen.name = "CampaignCustomization";
        _screen.gameObject.SetActive(false);
        _screen.enabled = false;
        CreationIdentity.RemoveInheritedPreviewModels(_screen);
        CreationIdentity.RemoveInheritedFaceCards(_screen._headSelectionState);
        UiElements.Stretch((RectTransform)_screen.transform);
        _screen.gameObject.SetActive(true);
        _screen._canvasGroup.alpha = 1;
        _screen._canvasGroup.interactable = true;
        _screen._canvasGroup.blocksRaycasts = true;
        _screen._nextButton.gameObject.SetActive(false);
        _screen._backButton.gameObject.SetActive(false);
        _screen._sideSelectionState.StateCanvasGroup.gameObject.SetActive(false);
        var head = _screen._headSelectionState;
        Owners.Add(head, this);
        _screen.transform.Find("SideDescriptions")?.gameObject.SetActive(false);
        _screen.transform.Find("FaceSelection/AuthorizationText")?.gameObject.SetActive(false);
        foreach (var preview in _screen.GetComponentsInChildren<PlayerProfilePreview>(true))
            if (preview != head._preview)
                preview.gameObject.SetActive(false);
        head.StateCanvasGroup.gameObject.SetActive(true);
        head.StateCanvasGroup.alpha = 0;
        head.StateCanvasGroup.interactable = false;
        var data = new CreateProfileOperation.PreliminaryProfileData
        {
            Side = _profile.Side,
            Nickname = _profile.Info.Nickname,
            HeadId = _profile.Customization[EBodyModelPart.Head],
            VoiceId = _profile.Customization[EBodyModelPart.Voice],
        };
        head.Init(
            data,
            new Dictionary<EPlayerSide, Profile> { [_profile.Side] = _profile.Clone() },
            new PlayerIconCreatorMaterialChanger(_screen._materialSettings),
            _profile.Info.Nickname
        );
        head._nicknameField.gameObject.SetActive(false);
        Layout(head);
        var statusRect = UiElements.Rect("AppearanceStatus", _host, 650, 32);
        statusRect.anchorMin = statusRect.anchorMax = new Vector2(1, 0);
        statusRect.pivot = new Vector2(1, 0);
        statusRect.anchoredPosition = new Vector2(-32, 16);
        Status = statusRect.gameObject.AddComponent<TextMeshProUGUI>();
        Status.font = head._nicknameField.GetComponentInChildren<TextMeshProUGUI>(true).font;
        Status.fontSize = 18;
        Status.alignment = TextAlignmentOptions.BottomRight;
        Status.raycastTarget = false;
        Status.text = "LOADING APPEARANCE...";
        await head.ShowState();
        if (_disposed)
            return;
        head._nicknameField.gameObject.SetActive(false);
        RestoreVoice();
        head.StateCanvasGroup.alpha = 1;
        head.StateCanvasGroup.interactable = true;
        head.StateCanvasGroup.blocksRaycasts = true;
        Status.text = "";
        Status.transform.SetAsLastSibling();
        _ready = true;
    }

    private void Layout(HeadSelectionState head)
    {
        var face = (RectTransform)_screen!.transform.Find("FaceSelection/LeftPanel/FaceSelector");
        face.anchorMin = Vector2.zero;
        face.anchorMax = Vector2.one;
        face.offsetMin = new Vector2(75, 100);
        face.offsetMax = new Vector2(-20, -85);
        var voice = (RectTransform)head._voiceSelector.transform.parent;
        voice.anchorMin = voice.anchorMax = Vector2.zero;
        voice.pivot = Vector2.zero;
        voice.anchoredPosition = new Vector2(75, 4);
        voice.sizeDelta = new Vector2(443, 80);
        var speaker = voice.Find("Icon").gameObject;
        var play = speaker.GetComponent<Button>() ?? speaker.AddComponent<Button>();
        play.targetGraphic = speaker.GetComponent<Image>();
        play.onClick.RemoveAllListeners();
        play.onClick.AddListener(async () =>
        {
            try
            {
                await head.PlayVoice(head._voiceSelector.CurrentIndex);
            }
            catch (Exception exception)
            {
                Plugin.Error(exception);
            }
        });
        var grid = head._faceCardsViewPort.GetComponent<GridLayoutGroup>();
        if (!grid)
            throw new InvalidOperationException("The native face-card grid is unavailable.");
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        // The installed native card assets are 3D portraits, including all eight heads per faction.
        var viewport = (RectTransform)head._faceCardsViewPort.parent;
        Canvas.ForceUpdateCanvases();
        var aspect = grid.cellSize.y / grid.cellSize.x;
        var rows = (Singleton<CustomizationSolver>.Instance.GetAvailableHeads(_profile.Side).AsValueEnumerable().Count() + 2) / 3;
        var width = Mathf.Min(
            (viewport.rect.width - grid.padding.horizontal - grid.spacing.x * 2) / 3,
            (viewport.rect.height - grid.padding.vertical - grid.spacing.y * (rows - 1)) / rows / aspect
        );
        grid.cellSize = new Vector2(width, width * aspect);
        // Move the native head camera's viewport up into the space formerly reserved for creation controls.
        var preview = (RectTransform)_screen.transform.Find("FaceSelection/PreviewPosition");
        preview.offsetMin = new Vector2(preview.offsetMin.x, 25);
        preview.offsetMax = new Vector2(preview.offsetMax.x, -55);
    }

    internal (string Head, string Voice) Selection()
    {
        var head = Head!;
        return (
            head._headTemplates[head._selectedHeadIndex].Key.ToString(),
            head._voiceTemplates[head._voiceSelector.CurrentIndex].Key.ToString()
        );
    }

    internal void Restore()
    {
        if (!_ready || _disposed)
            return;
        var index = Head!._headTemplates.FindIndex(value => value.Key == _profile.Customization[EBodyModelPart.Head]);
        if (index >= 0)
        {
            Head.HeadValueChangedHandler(index, true);
            foreach (var card in Head._faceCards)
            {
                var selected = card == Head._faceCards[index];
                card._toggle.SetIsOnWithoutNotify(selected);
                card.SetSelected(selected);
            }
        }
        RestoreVoice();
    }

    private void RestoreVoice()
    {
        var head = Head!;
        var index = head._voiceTemplates.FindIndex(value => value.Key == _profile.Customization[EBodyModelPart.Voice]);
        head._selectedVoiceIndex = index;
        head._voiceSelector.UpdateValue(index, false);
        head._profileData.VoiceId = _profile.Customization[EBodyModelPart.Voice];
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_screen)
        {
            _screen!.transform.SetParent(null, false);
            _screen.gameObject.SetActive(false);
        }
        Release();
    }

    internal Task UpdatePreview() => Track(Render(++_previewGeneration));

    private async Task Render(int generation)
    {
        var head = Head!;
        await head._preview.Show(head._previewProfile);
        // Native model loads may complete after a newer selection or a closed screen.
        if (_disposed || generation != _previewGeneration || !head._preview.PlayerModelView.LoadingComplete)
            return;
        head._materialChanger.ChangeMaterials(head._preview.PlayerModelView.PlayerBody);
        head.HideSlots();
    }

    internal Task PlayVoice(int index) => Track(Play(index));

    private async Task Play(int index)
    {
        var head = Head!;
        if (_disposed || index < 0 || index >= head._voiceTemplates.Count)
            return;
        var template = head._voiceTemplates[index];
        if (!head._voices.TryGetValue(index, out var bank))
        {
            bank = await Singleton<PlayerVoiceLoader>.Instance.TakeVoice(template.Value.Name);
            if (_disposed || bank == null)
                return;
            head._voices[index] = bank;
        }
        if (_disposed || index != head._voiceSelector.CurrentIndex || bank.Clips.Length == 0)
            return;
        head._profileData.VoiceId = template.Key;
        await Singleton<GUISounds>.Instance.ForcePlaySound(bank.Clips[UnityEngine.Random.Range(0, bank.Clips.Length)].Clip);
    }

    private async Task Track(Task task)
    {
        _pending.Add(task);
        try
        {
            await task;
        }
        finally
        {
            _pending.Remove(task);
        }
    }

    private async void Release()
    {
        try
        {
            try
            {
                await _load;
            }
            catch (Exception exception)
            {
                Plugin.Error(exception);
            }
            try
            {
                await Task.WhenAll(_pending.AsValueEnumerable().ToArray());
            }
            catch (Exception exception)
            {
                Plugin.Error(exception);
            }
            if (_screen)
            {
                _screen!._headSelectionState.Close();
                foreach (var preview in _screen.GetComponentsInChildren<PlayerProfilePreview>(true))
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
                Owners.Remove(_screen!._headSelectionState);
                UnityEngine.Object.Destroy(_screen.gameObject);
            }
            if (Status)
                UnityEngine.Object.Destroy(Status!.gameObject);
        }
    }
}
