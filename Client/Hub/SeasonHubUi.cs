using EFT.UI;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.UI.Audio;
using WTT.Campaigns.UI.BattlePass;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;
using WTT.Campaigns.UI.Profiles;
using WTT.Campaigns.UI.Screens;
using ZLinq;

namespace WTT.Campaigns.Client.Hub;

public sealed partial class SeasonHubUi : MonoBehaviour
{
    internal static SeasonHubUi Instance = null!;
    private readonly Dictionary<string, Task<Sprite>> _images = new(StringComparer.Ordinal);
    private readonly HashSet<string> _allowedImages = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loading;
    private GameObject? _canvas;
    private GameObject? _banner;
    private MenuScreen? _menu;
    private string? _bannerSeason;
    private long _bannerRevision;
    private SeasonsHubScreen? _screen;
    private bool _opening;
    private bool _destroyed;
    private int _generation;
    private int _blockedThrough = -1;
    private string? _openedProfile;

    internal bool IsOpen
    {
        get { return _screen != null && _screen.Root && _screen.Root.activeSelf; }
    }
    internal bool InputBlocked
    {
        get { return IsOpen || Time.frameCount <= _blockedThrough; }
    }
    internal static bool Available
    {
        get { return Plugin.Current?.ActiveMode == "seasonal" && !Plugin.InRaid && !Plugin.Busy; }
    }
    private AssetBundle Bundle
    {
        get { return SeasonUi.Instance.UiBundle; }
    }
    private Font Font
    {
        get { return Bundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf"); }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (_menu && !Plugin.Busy && (_bannerSeason != Plugin.Current?.SeasonId || _bannerRevision != Plugin.Current?.PackRevision))
        {
            AttachMenu(_menu!);
        }
        if (_banner)
        {
            _banner!.SetActive(Available && !SeasonUi.Instance.IsOpen && !IsOpen);
        }

        if (!IsOpen)
        {
            return;
        }

        if (
            Plugin.InRaid
            || Plugin.Current?.ActiveMode != "seasonal"
            || Plugin.Current.EffectiveProfileId != _openedProfile
            || (!Available && !_transacting)
            || SeasonUi.Instance.IsOpen
        )
        {
            Close();
            return;
        }
        _screen!.Fit();
        if (_screen.HasTutorial)
        {
            UpdateTutorialInput();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_transacting)
            {
                return;
            }
            if (_screen!.HasDialog)
            {
                _screen.DismissDialog();
                return;
            }
            SeasonUi.Instance.PlayInterfaceSound(InterfaceSound.Back);
            Close();
        }
        else if (!_opening && !_transacting && !_screen!.HasDialog && Input.GetKeyDown(KeyCode.Q))
        {
            _screen.ChangePage(-1);
        }
        else if (!_opening && !_transacting && !_screen!.HasDialog && Input.GetKeyDown(KeyCode.E))
        {
            _screen.ChangePage(1);
        }
    }

    internal void AttachMenu(MenuScreen menu)
    {
        _menu = menu;
        if (
            _banner
            && _banner!.transform.IsChildOf(menu.transform)
            && _bannerSeason == Plugin.Current?.SeasonId
            && _bannerRevision == Plugin.Current?.PackRevision
        )
        {
            return;
        }

        if (_banner)
        {
            _banner!.SetActive(false);
            Destroy(_banner);
        }
        _bannerSeason = Plugin.Current?.SeasonId;
        _bannerRevision = Plugin.Current?.PackRevision ?? 0;

        var source = menu._playerButton;
        var parent = source.transform.parent;
        var rect = UiElements.Rect("SeasonalHubBanner", parent, 440, 112);
        _banner = rect.gameObject;
        rect.SetSiblingIndex(0);
        if (parent.GetComponent<VerticalLayoutGroup>())
        {
            var layout = _banner.AddComponent<LayoutElement>();
            layout.preferredWidth = 440;
            layout.preferredHeight = 112;
            layout.flexibleHeight = 0;
        }
        else
        {
            var sourceRect = (RectTransform)source.transform;
            rect.anchorMin = sourceRect.anchorMin;
            rect.anchorMax = sourceRect.anchorMax;
            // Leave the native beta notice between the banner and the menu options.
            rect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0, 300);
        }
        var banner = _banner.AddComponent<SeasonBanner>();
        var sound = _banner.AddComponent<HubBannerSound>();
        sound.Initialize(Bundle.LoadAsset<AudioClip>("assets/mods/wtt-campaigns.assets/audio/hub-hover-loop.wav"));
        if (Plugin.Current?.LegacyBranding != false)
        {
            banner.Initialize(Font, Artwork);
        }
        else
        {
            var image = banner.InitializeCustom(Font, Plugin.Current.SeasonName);
            if (Plugin.Current.BannerImage.Length == 24)
            {
                LoadBanner(Plugin.Current.BannerImage, image);
            }
        }
        banner.Clicked = () =>
        {
            if (Available)
            {
                PlaySound("hub-click");
                Open();
            }
        };
        banner.HoverChanged = over =>
        {
            sound.Hover(over);
            if (over && Available)
            {
                PlaySound("hub-hover");
            }
        };
        _banner.SetActive(Available && !SeasonUi.Instance.IsOpen);
    }

    internal async void Open()
    {
        if (!Available || _opening || IsOpen)
        {
            return;
        }

        try
        {
            EnsureScreen();
            RestorePending();
            _openedProfile = Plugin.Current!.EffectiveProfileId;
            _tutorialOffered = false;
            _screen!.Open();
            await LoadState();
            if (_pendingBody != null && _pendingProfile == Plugin.Current?.EffectiveProfileId)
            {
                await SendPending();
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    private void EnsureScreen()
    {
        if (_screen != null)
        {
            return;
        }

        _canvas = new GameObject("SeasonHubCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(_canvas);
        var canvas = _canvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 29001;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        _screen = new SeasonsHubScreen(_canvas.transform, Font, Artwork)
        {
            CloseRequested = Close,
            RetryRequested = Retry,
            SoundRequested = SeasonUi.Instance.PlayInterfaceSound,
            ImageRequested = LoadImage,
            PerkIconRequested = SeasonUi.Instance.LoadIcon,
            VideoRequested = PlayVideo,
            TransactionRequested = Transact,
            TutorialCompleted = CompleteTutorial,
        };
    }

    private async void Retry()
    {
        if (_pendingBody != null && !_transacting)
        {
            await SendPending();
            return;
        }
        if (IsOpen && !_opening)
        {
            await LoadState();
        }
    }

    private async Task LoadState()
    {
        _opening = true;
        var generation = ++_generation;
        _loading?.Cancel();
        _loading?.Dispose();
        ReleaseImages();
        _loading = new CancellationTokenSource();
        _screen!.ShowMessage("Loading campaign...", false);
        try
        {
            var snapshot = Plugin.Current ?? throw new InvalidOperationException("Select your character before opening the Battle Pass.");
            var profileId = snapshot.EffectiveProfileId;
            var seasonId = snapshot.SeasonId;
            if (Plugin.App?.Session?.Profile?.Id != profileId)
            {
                throw new InvalidOperationException("The selected character has not finished loading. Select your character again.");
            }
            var raw = await RequestHandler.PostJsonAsync(
                "/wtt-campaigns/hub",
                JsonConvert.SerializeObject(
                    new
                    {
                        ProtocolVersion = 2,
                        SeasonId = seasonId,
                        CharacterId = profileId,
                    }
                )
            );
            if (
                _destroyed
                || generation != _generation
                || !IsOpen
                || !Available
                || Plugin.Current?.EffectiveProfileId != profileId
                || Plugin.Current.SeasonId != seasonId
                || Plugin.App?.Session?.Profile?.Id != profileId
            )
            {
                return;
            }

            var data = JsonConvert.DeserializeObject<HubState>(raw) ?? throw new InvalidDataException("Empty campaign hub response.");
            if (data.Error.Length > 0)
            {
                throw new InvalidOperationException(data.Error);
            }
            if (data.SeasonId != seasonId)
            {
                throw new InvalidDataException("The server returned a different campaign's Battle Pass. Select your character again.");
            }
            if (data.Pages.Length == 0)
            {
                throw new InvalidDataException("Campaign catalogue is unavailable.");
            }

            data.SeasonName = Plugin.Localized(data.SeasonId + " name", data.SeasonName);
            foreach (var document in data.Documents)
            {
                document.Name = Plugin.Localized(document.Id + " name", document.Name);
            }

            foreach (var reward in data.Pages.AsValueEnumerable().SelectMany(p => p.Rewards).Concat(data.SeasonalRewards))
            {
                reward.Name = Plugin.Localized(reward.Id + " name", reward.Name);
                reward.Description = Plugin.Localized(reward.Id + " description", reward.Description);
            }
            for (var i = 0; i < data.Slides.Length; i++)
            {
                data.Slides[i].Text = Plugin.Localized(data.SeasonId + " slide " + i + " text", data.Slides[i].Text);
            }
            _allowedImages.Clear();
            foreach (
                var id in data
                    .Pages.AsValueEnumerable()
                    .SelectMany(p => p.Rewards)
                    .Concat(data.SeasonalRewards)
                    .SelectMany(r => new[] { r.Image, r.BigImage })
                    .Concat(data.Documents.AsValueEnumerable().SelectMany(d => new[] { d.Image, d.UnavailableImage }))
                    .Concat(data.Slides.AsValueEnumerable().Select(slide => slide.Image))
                    .Append(data.BadgeImage)
                    .Append(data.BannerImage)
                    .Where(id => id.Length == 24 && id.AsValueEnumerable().All(Uri.IsHexDigit))
                    .Append(data.UniversalImage)
                    .Append(data.UniversalUnavailableImage)
            )
            {
                if (id.Length != 24 || id.AsValueEnumerable().Any(c => !Uri.IsHexDigit(c)))
                {
                    throw new InvalidDataException("Invalid hub image identifier.");
                }

                _allowedImages.Add(id);
            }
            _screen.SetState(data, SeasonUi.Presentation(Plugin.Current!).Perks);
            OfferTutorial();
        }
        catch (Exception exception)
        {
            if (generation == _generation && IsOpen)
            {
                _screen.ShowMessage("Unable to load the campaign. Check the local server and try again.", true);
                Plugin.Error(exception);
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _opening = false;
            }
        }
    }

    private Sprite? Artwork(string name)
    {
        return Bundle.LoadAsset<Sprite>("assets/mods/wtt-campaigns.assets/hubartwork/" + name + ".png");
    }

    private void PlayVideo(string name, RawImage target, bool loop, Image? fallback)
    {
        var clip = Bundle.LoadAsset<VideoClip>("assets/mods/wtt-campaigns.assets/hubmedia/" + name.ToLowerInvariant());
        if (clip)
        {
            target.gameObject.AddComponent<HubVideo>().Initialize(clip, target, loop, fallback);
        }
        else
        {
            Plugin.LogInfo("Campaign hub video asset missing: " + name);
        }
    }

    private void PlaySound(string name)
    {
        var clip = Bundle.LoadAsset<AudioClip>("assets/mods/wtt-campaigns.assets/audio/" + name + ".wav");
        if (clip && Comfort.Common.Singleton<GUISounds>.Instantiated)
        {
            Comfort.Common.Singleton<GUISounds>.Instance.PlaySound(clip);
        }
    }

    private async void LoadImage(string id, Image target)
    {
        var cacheKey = Plugin.Current?.SeasonId + ":" + Plugin.Current?.PackRevision + ":" + id;
        var generation = _generation;
        if (!_allowedImages.Contains(id) || _loading == null)
        {
            return;
        }

        try
        {
            if (!_images.TryGetValue(cacheKey, out var task))
            {
                _images[cacheKey] = task = FetchImage(id, _loading.Token);
            }

            var sprite = await task;
            if (!_destroyed && generation == _generation && IsOpen && target)
            {
                target.sprite = sprite;
                target.color = Color.white;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation != _generation)
            {
                return;
            }

            _images.Remove(cacheKey);
            if (target)
            {
                var text = new UiElements(Font).Label(
                    target.transform,
                    "ImageUnavailable",
                    "Image unavailable",
                    14,
                    target.rectTransform.rect.width,
                    40
                );
                text.alignment = TextAnchor.MiddleCenter;
            }
            Plugin.Error(exception);
        }
    }

    private Sprite? _bannerSprite;

    private async void LoadBanner(string id, Image target)
    {
        try
        {
            var sprite = await FetchImage(id, CancellationToken.None);
            if (!target)
            {
                Destroy(sprite.texture);
                Destroy(sprite);
                return;
            }
            if (_bannerSprite)
            {
                Destroy(_bannerSprite!.texture);
                Destroy(_bannerSprite);
            }
            _bannerSprite = sprite;
            target.sprite = sprite;
            target.color = Color.white;
        }
        catch (Exception e)
        {
            Plugin.Error(e);
        }
    }

    private async Task<Sprite> FetchImage(string id, CancellationToken cancellation)
    {
        var texture = await SeasonImageLoader.LoadAsync(SeasonImageLoader.PathFor("hub-images", id), cancellation);
        if (cancellation.IsCancellationRequested)
        {
            Destroy(texture);
            cancellation.ThrowIfCancellationRequested();
        }
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
    }

    internal void Close()
    {
        ++_generation;
        _opening = false;
        _loading?.Cancel();
        _screen?.Close();
        _blockedThrough = Time.frameCount + 1;
        ReleaseImages();
    }

    private void ReleaseImages()
    {
        foreach (var task in _images.Values.AsValueEnumerable().Where(t => t.Status == TaskStatus.RanToCompletion))
        {
            Destroy(task.Result.texture);
            Destroy(task.Result);
        }
        _images.Clear();
    }

    private void OnDestroy()
    {
        _destroyed = true;
        if (_bannerSprite)
        {
            Destroy(_bannerSprite!.texture);
            Destroy(_bannerSprite);
        }
        Close();
        _loading?.Dispose();
        _screen?.Dispose();
        if (_canvas)
        {
            Destroy(_canvas);
        }

        if (_banner)
        {
            Destroy(_banner);
        }
    }
}
