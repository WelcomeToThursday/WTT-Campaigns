using EFT;
using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Customization;

public sealed class CustomizationTab : MonoBehaviour, ITabController
{
    internal const EInventoryTab TabId = (EInventoryTab)1001;
    private Tab? _tab;
    private RectTransform? _host;
    private Profile? _profile;
    private string? _sessionId;
    private AppearanceView? _view;
    private Task _loading = Task.CompletedTask;
    private Task<bool>? _saving;
    private (string Head, string Voice) _observed;
    private float _changedAt;
    private bool _closing;
    private bool _flushing;
    private Sprite? _icon;
    private Texture2D? _texture;

    internal void Initialize(InventoryScreen screen, InventoryScreen.InventoryScreenController controller)
    {
        ReleaseView();
        _profile = controller.Profile;
        _sessionId = Plugin.SessionId;
        _closing = false;
        _saving = null;
        if (!_tab)
        {
            var source = screen._tabDictionary[EInventoryTab.Skills];
            _tab = Instantiate(source, source.transform.parent, false);
            _tab.name = "CampaignCustomizationTab";
            foreach (var localized in _tab.GetComponentsInChildren<LocalizedText>(true))
                localized.enabled = false;
            foreach (var label in _tab.GetComponentsInChildren<TextMeshProUGUI>(true))
                label.text = "CUSTOMIZATION";
            using var resource =
                typeof(CustomizationTab).Assembly.GetManifestResourceStream("WTT.Campaigns.Customization.face.png")
                ?? throw new InvalidOperationException("The live customization tab artwork is missing.");
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!_texture.LoadImage(bytes.ToArray()))
                throw new InvalidDataException("Invalid customization artwork.");
            _icon = Sprite.Create(_texture, new Rect(0, 0, _texture.width, _texture.height), new Vector2(.5f, .5f));
            foreach (var version in new[] { _tab._normalVersion, _tab._selectedVersion })
            foreach (var image in version.GetComponentsInChildren<Image>(true))
                if (image.name == "Icon")
                {
                    image.sprite = _icon;
                    image.preserveAspect = true;
                }

            // Both collections must retain the same order: native CloseAction converts a tab index to its dictionary key.
            var tabs = new Dictionary<EInventoryTab, Tab>();
            foreach (var pair in screen._tabDictionary)
            {
                if (pair.Key == EInventoryTab.Skills)
                    tabs.Add(TabId, _tab);
                tabs.Add(pair.Key, pair.Value);
            }
            screen._tabDictionary = tabs;
            FitTabRow(tabs.Values.AsValueEnumerable().ToArray());
            _host = UiElements.Rect("CampaignCustomizationContent", screen._skillsAndMasteringScreen.transform.parent, 0, 0);
            UiElements.Stretch(_host);
            // Keep the native top tabs and bottom task bar available.
            _host.offsetMin = new Vector2(0, 35);
            _host.offsetMax = new Vector2(0, -45);
        }
        _tab!.Init(this);
        _tab.UpdateVisual(false);
        var available = !controller.InRaid && !controller.IsInventoryBlocked && _profile.Side != EPlayerSide.Savage;
        _tab.SetInteractable(available);
        _host!.gameObject.SetActive(false);
        if (!available && controller.LastSelectedTab == TabId)
            controller.LastSelectedTab = EInventoryTab.Gear;
    }

    private static void FitTabRow(Tab[] tabs)
    {
        // Native tabs overlap their slanted ends and change sibling order on selection. Position by coordinates, not siblings.
        var native = tabs.AsValueEnumerable().Where(tab => tab.name != "CampaignCustomizationTab").ToArray();
        var first = (RectTransform)native[0].transform;
        var left = native.AsValueEnumerable().Min(tab => ((RectTransform)tab.transform).anchoredPosition.x);
        var width = native.AsValueEnumerable().Max(tab => ((RectTransform)tab.transform).rect.width);
        if (width <= 0)
            throw new InvalidOperationException("The Character tab layout is unavailable.");
        for (var i = 0; i < tabs.Length; i++)
        {
            // Native TabSizeController only knows its original children. Stop those width notifications
            // and size the complete row together so it cannot move the new tab underneath Skills.
            var element = tabs[i].GetComponent<TabElement>();
            if (element)
                element.enabled = false;
            var rect = (RectTransform)tabs[i].transform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rect.anchoredPosition = new Vector2(left + i * (width - 26), first.anchoredPosition.y);
            foreach (var label in tabs[i].GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                var fitter = label.GetComponent<ContentSizeFitter>();
                if (fitter)
                    fitter.enabled = false;
                label.rectTransform.anchorMin = new Vector2(0, .5f);
                label.rectTransform.anchorMax = new Vector2(1, .5f);
                label.rectTransform.pivot = new Vector2(.5f, .5f);
                label.rectTransform.sizeDelta = new Vector2(-75, 24);
                label.rectTransform.anchoredPosition = new Vector2(13.5f, 1);
                label.enableAutoSizing = true;
                label.fontSizeMin = 11;
                label.fontSizeMax = label.fontSize;
            }
        }
    }

    public void Show()
    {
        if (_closing || Plugin.InRaid || _profile == null || !_tab!.Interactable)
            return;
        _host!.gameObject.SetActive(true);
        if (_view == null)
        {
            _view = new AppearanceView(_host, _profile);
            _loading = Load(_view);
        }
    }

    private async Task Load(AppearanceView view)
    {
        try
        {
            await view.Load();
            if (_view == view)
                _observed = view.Selection();
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            Message(exception.Message, true);
        }
    }

    private void Update()
    {
        if (_closing || _flushing || _view?.Ready != true || !_host!.gameObject.activeInHierarchy)
            return;
        var selected = _view.Selection();
        if (selected != _observed)
        {
            _observed = selected;
            _changedAt = Time.unscaledTime;
        }
        if (Time.unscaledTime - _changedAt >= .45f && Dirty && (_saving == null || _saving.IsCompleted))
            _saving = Save();
    }

    private bool Dirty => _view?.SelectionReady == true && _profile != null && _view.Selection() != Confirmed;
    private (string Head, string Voice) Confirmed =>
        (_profile!.Customization[EBodyModelPart.Head].ToString(), _profile.Customization[EBodyModelPart.Voice].ToString());

    private async Task<bool> Save()
    {
        if (!Dirty)
            return true;
        var view = _view!;
        var profile = _profile!;
        var selection = view.Selection();
        var sessionId = _sessionId;
        try
        {
            if (Plugin.InRaid || sessionId != Plugin.SessionId || profile != Plugin.App?.Session?.Profile)
                throw new InvalidOperationException("The active character changed. Reopen Customization.");
            Message("SAVING...", false);
            var response = await RequestHandler.PostJsonAsync(
                "/wtt-campaigns/appearance",
                JsonConvert.SerializeObject(
                    new
                    {
                        ProfileId = sessionId,
                        HeadId = selection.Head,
                        VoiceId = selection.Voice,
                    }
                )
            );
            var result =
                JsonConvert.DeserializeObject<AppearanceResponse>(response)
                ?? throw new InvalidDataException("The appearance service returned an empty response.");
            if (!string.IsNullOrEmpty(result.Error))
                throw new InvalidOperationException(result.Error);
            if (result.ProfileId != sessionId || result.HeadId != selection.Head || result.VoiceId != selection.Voice)
                throw new InvalidDataException("The server did not confirm the selected appearance.");
            profile.Customization[EBodyModelPart.Head] = result.HeadId;
            profile.Customization[EBodyModelPart.Voice] = result.VoiceId;
            if (_view == view)
                Message("", false);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            if (_view == view)
            {
                view.Restore();
                _observed = Confirmed;
                Message("Appearance was not saved. " + exception.Message, true);
            }
            return false;
        }
    }

    internal async Task<bool> Flush()
    {
        _flushing = true;
        var view = _view;
        try
        {
            await _loading;
            if (view?.Head != null)
                view.Head.StateCanvasGroup.interactable = false;
            if (_saving != null && !await _saving)
            {
                _saving = null;
                return false;
            }
            if (Dirty)
            {
                _saving = Save();
                return await _saving;
            }
            return true;
        }
        finally
        {
            _flushing = false;
            if (_view == view && view?.Head != null)
                view.Head.StateCanvasGroup.interactable = true;
        }
    }

    public async Task<bool> TryHide()
    {
        if (!await Flush())
            return false;
        if (_host)
            _host!.gameObject.SetActive(false);
        ReleaseView();
        return true;
    }

    private void Message(string message, bool error)
    {
        if (_view?.Status)
        {
            _view!.Status!.text = message;
            _view.Status.color = error ? UiElements.Negative : UiElements.Ink;
        }
        else if (error)
            EFT.UI.PreloaderUI.Instance.ShowErrorScreen("Customization", message);
    }

    internal void Close()
    {
        _closing = true;
        ReleaseView();
        if (_host)
            _host!.gameObject.SetActive(false);
    }

    private void ReleaseView()
    {
        _view?.Dispose();
        _view = null;
    }

    private void OnDestroy()
    {
        Close();
        if (_host)
            Destroy(_host!.gameObject);
        if (_tab)
            Destroy(_tab!.gameObject);
        if (_icon)
            Destroy(_icon);
        if (_texture)
            Destroy(_texture);
    }

    private sealed class AppearanceResponse
    {
        public string? Error { get; set; }
        public string ProfileId { get; set; } = "";
        public string HeadId { get; set; } = "";
        public string VoiceId { get; set; } = "";
    }
}
