using SeasonalPerks.Client.UI;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Video;

namespace SeasonalPerks.Client.Story;

public sealed class StoryCinematicRuntime : MonoBehaviour
{
    internal static StoryCinematicRuntime Instance = null!;
    private StoryPresentationSurface? _surface;
    private AssetBundle? _bundle;
    private GameObject? _media;
    private VideoPlayer? _video;
    private PlayableDirector? _director;
    private string _binding = "";
    private string _character = "";
    private bool _inRaid;
    private bool _started;
    private bool _ending;
    private float _startedAt;
    private int _blockedThrough;
    internal bool InputBlocked
    {
        get { return _surface != null || Time.frameCount <= _blockedThrough; }
    }

    private void Awake()
    {
        Instance = this;
    }

    internal void Play(string mediaId, string binding)
    {
        if (_surface != null || !StoryClient.Available)
        {
            return;
        }
        _binding = binding;
        _character = Plugin.Current!.EffectiveProfileId;
        _inRaid = Plugin.InRaid;
        _startedAt = Time.realtimeSinceStartup;
        try
        {
            var media = StoryMediaStore.Find(mediaId);
            _bundle = StoryMediaStore.Open(media.Bundle, media.Sha256);
            _surface = new StoryPresentationSurface("Seasonal cinematic", 32010);
            var font = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/seasonalperks.assets/fonts/bender.ttf");
            var skip = new UiElements(font).Button(_surface.Root.transform, "Skip", 150, 0, 0, () => End("skip"), 42);
            var rect = (RectTransform)skip.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-105, 60);
            if (media.Kind == "Video")
            {
                _media = new GameObject("Story video");
                _video = _media.AddComponent<VideoPlayer>();
                _video.playOnAwake = false;
                _video.isLooping = false;
                _video.renderMode = VideoRenderMode.RenderTexture;
                _video.targetTexture = _surface.Texture;
                _surface.Background.color = Color.white;
                _video.clip =
                    _bundle.LoadAsset<VideoClip>(media.Asset) ?? throw new InvalidDataException("The cinematic video is missing.");
                _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
                var source = _media.AddComponent<AudioSource>();
                StoryAudio.Configure(source);
                _video.SetTargetAudioSource(0, source);
                _video.loopPointReached += CompletedVideo;
                _video.errorReceived += FailedVideo;
                _video.prepareCompleted += PreparedVideo;
                _video.Prepare();
            }
            else if (media.Kind == "Cinematic")
            {
                var prefab =
                    _bundle.LoadAsset<GameObject>(media.Asset) ?? throw new InvalidDataException("The cinematic prefab is missing.");
                if (prefab.activeSelf)
                {
                    throw new InvalidDataException(
                        "The cinematic prefab root must be inactive so its camera and playback can be prepared."
                    );
                }
                _media = Instantiate(prefab);
                _director =
                    _media.GetComponentInChildren<PlayableDirector>(true)
                    ?? throw new InvalidDataException("The cinematic requires a PlayableDirector.");
                var camera = _media.GetComponentsInChildren<Camera>(true).Single(c => c.name == "StoryCamera");
                _surface.UseCamera(camera);
                _director.playOnAwake = false;
                // Hold preserves the end time, so an external Stop cannot masquerade
                // as successful playback when the graph resets its clock.
                _director.extrapolationMode = DirectorWrapMode.Hold;
                _director.stopped += CompletedDirector;
                _media.SetActive(true);
                _started = true;
                _director.Play();
                if (double.IsNaN(_director.duration) || double.IsInfinity(_director.duration) || _director.duration <= 0)
                {
                    throw new InvalidDataException("The cinematic requires a finite duration.");
                }
            }
            else
            {
                throw new InvalidDataException("This media is not a cinematic or video.");
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            End("interrupt");
        }
    }

    private void PreparedVideo(VideoPlayer player)
    {
        if (_video != player || _ending)
        {
            return;
        }
        _started = true;
        player.Play();
    }

    private void CompletedVideo(VideoPlayer player)
    {
        End("complete");
    }

    private void FailedVideo(VideoPlayer player, string message)
    {
        Plugin.LogInfo("Story video failed: " + message);
        End("interrupt");
    }

    private void CompletedDirector(PlayableDirector director)
    {
        End("interrupt");
    }

    private void Update()
    {
        if (_surface == null || _ending)
        {
            return;
        }
        if (
            !StoryClient.Available
            || _character != Plugin.Current!.EffectiveProfileId
            || _inRaid != Plugin.InRaid
            || !_surface.Root
            || _inRaid && Plugin.Player?.HealthController?.IsAlive != true
        )
        {
            End("interrupt");
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            End("skip");
        }
        else if (_director && _started && _director!.time >= _director.duration)
        {
            End("complete");
        }
        else if (!_started && Time.realtimeSinceStartup - _startedAt > 30)
        {
            End("interrupt");
        }
    }

    private async void End(string result)
    {
        if (_ending)
        {
            return;
        }
        _ending = true;
        var binding = _binding;
        var character = _character;
        Clear();
        try
        {
            if (binding.Length > 0 && StoryClient.Available && Plugin.Current!.EffectiveProfileId == character)
            {
                await StoryClient.Mutate("raid", binding, result);
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        finally
        {
            _ending = false;
        }
    }

    private void Clear()
    {
        _blockedThrough = Time.frameCount + 1;
        if (_video)
        {
            _video!.loopPointReached -= CompletedVideo;
            _video.errorReceived -= FailedVideo;
            _video.prepareCompleted -= PreparedVideo;
            _video.Stop();
        }
        if (_director)
        {
            _director!.stopped -= CompletedDirector;
            _director.Stop();
        }
        _video = null;
        _director = null;
        if (_media)
        {
            _media!.SetActive(false);
            Destroy(_media);
        }
        _media = null;
        _surface?.Dispose();
        _surface = null;
        if (_bundle)
        {
            StoryMediaStore.Close(_bundle!);
        }
        _bundle = null;
        _binding = "";
        _started = false;
    }

    private void OnDestroy()
    {
        Clear();
    }
}
