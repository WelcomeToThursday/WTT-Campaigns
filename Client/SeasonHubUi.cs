using EFT.UI;
using Newtonsoft.Json;
using SeasonalPerks.UI.Audio;
using SeasonalPerks.UI.BattlePass;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Profiles;
using SeasonalPerks.UI.Screens;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace SeasonalPerks.Client;

public sealed class SeasonHubUi : MonoBehaviour
{
    internal static SeasonHubUi Instance = null!;
    private readonly Dictionary<string, Task<Sprite>> _images = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _imageSlots = new(6);
    private readonly HashSet<string> _allowedImages = new(StringComparer.Ordinal);
    private CancellationTokenSource? _loading;
    private GameObject? _canvas;
    private GameObject? _banner;
    private SeasonsHubScreen? _screen;
    private bool _opening;
    private bool _destroyed;
    private int _generation;
    private int _blockedThrough = -1;

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
        get { return Bundle.LoadAsset<Font>("assets/mods/seasonalperks.assets/fonts/bender.ttf"); }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (_banner)
        {
            _banner!.SetActive(Available && !SeasonUi.Instance.IsOpen && !IsOpen);
        }

        if (!IsOpen)
        {
            return;
        }

        if (!Available || SeasonUi.Instance.IsOpen)
        {
            Close();
            return;
        }
        _screen!.Fit();
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SeasonUi.Instance.PlayInterfaceSound(InterfaceSound.Back);
            Close();
        }
        else if (!_opening && Input.GetKeyDown(KeyCode.Q))
        {
            _screen.ChangePage(-1);
        }
        else if (!_opening && Input.GetKeyDown(KeyCode.E))
        {
            _screen.ChangePage(1);
        }
    }

    internal void AttachMenu(MenuScreen menu)
    {
        if (_banner && _banner!.transform.IsChildOf(menu.transform))
        {
            return;
        }

        if (_banner)
        {
            Destroy(_banner);
        }

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
        sound.Initialize(Bundle.LoadAsset<AudioClip>("assets/mods/seasonalperks.assets/audio/hub-hover-loop.wav"));
        banner.Initialize(Font, Artwork, PlayVideo);
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
            _screen!.Open();
            await LoadState();
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
        };
    }

    private async void Retry()
    {
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
        _screen!.ShowMessage("Loading season...", false);
        try
        {
            var raw = await RequestHandler.PostJsonAsync("/seasonal-perks/hub", "{}");
            if (_destroyed || generation != _generation || !IsOpen || !Available)
            {
                return;
            }

            var data = JsonConvert.DeserializeObject<HubState>(raw) ?? throw new InvalidDataException("Empty seasonal hub response.");
            if (data.Pages.Length == 0)
            {
                throw new InvalidDataException("Season catalogue is unavailable.");
            }

            _allowedImages.Clear();
            foreach (
                var id in data
                    .Pages.SelectMany(p => p.Rewards)
                    .Concat(data.SeasonalRewards)
                    .SelectMany(r => new[] { r.Image, r.BigImage })
                    .Concat(data.Documents.SelectMany(d => new[] { d.Image, d.UnavailableImage }))
                    .Append(data.UniversalImage)
                    .Append(data.UniversalUnavailableImage)
            )
            {
                if (id.Length != 24 || id.Any(c => !Uri.IsHexDigit(c)))
                {
                    throw new InvalidDataException("Invalid hub image identifier.");
                }

                _allowedImages.Add(id);
            }
            _screen.SetState(data, SeasonUi.Presentation(Plugin.Current!).Perks);
        }
        catch (Exception exception)
        {
            if (generation == _generation && IsOpen)
            {
                _screen.ShowMessage("Unable to load the season. Check the local server and try again.", true);
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
        return Bundle.LoadAsset<Sprite>("assets/mods/seasonalperks.assets/hubartwork/" + name + ".png");
    }

    private void PlayVideo(string name, RawImage target, bool loop, Image? fallback)
    {
        var clip = Bundle.LoadAsset<VideoClip>("assets/mods/seasonalperks.assets/hubmedia/" + name.ToLowerInvariant());
        if (clip)
        {
            target.gameObject.AddComponent<HubVideo>().Initialize(clip, target, loop, fallback);
        }
        else
        {
            Plugin.LogInfo("Season hub video asset missing: " + name);
        }
    }

    private void PlaySound(string name)
    {
        var clip = Bundle.LoadAsset<AudioClip>("assets/mods/seasonalperks.assets/audio/" + name + ".wav");
        if (clip && Comfort.Common.Singleton<GUISounds>.Instantiated)
        {
            Comfort.Common.Singleton<GUISounds>.Instance.PlaySound(clip);
        }
    }

    private async void LoadImage(string id, Image target)
    {
        var generation = _generation;
        if (!_allowedImages.Contains(id) || _loading == null)
        {
            return;
        }

        try
        {
            if (!_images.TryGetValue(id, out var task))
            {
                _images[id] = task = FetchImage(id, _loading.Token);
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

            _images.Remove(id);
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

    private async Task<Sprite> FetchImage(string id, CancellationToken cancellation)
    {
        await _imageSlots.WaitAsync(cancellation);
        try
        {
            var bytes = await RequestHandler.GetDataAsync("/seasonal-perks/hub-images/" + id + ".png");
            cancellation.ThrowIfCancellationRequested();
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes) || texture.width > 4096 || texture.height > 4096)
            {
                Destroy(texture);
                throw new InvalidDataException("Invalid seasonal hub image.");
            }
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        }
        finally
        {
            _imageSlots.Release();
        }
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
        foreach (var task in _images.Values.Where(t => t.Status == TaskStatus.RanToCompletion))
        {
            Destroy(task.Result.texture);
            Destroy(task.Result);
        }
        _images.Clear();
    }

    private void OnDestroy()
    {
        _destroyed = true;
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
