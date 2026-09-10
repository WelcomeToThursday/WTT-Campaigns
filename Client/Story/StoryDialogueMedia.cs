using Cysharp.Threading.Tasks;
using SeasonalPerks.Shared.Story;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.UI;
using ZLinq;

namespace SeasonalPerks.Client.Story;

internal sealed class StoryDialogueMedia : IDisposable
{
    private readonly Dictionary<string, AssetBundle> _bundles = new();
    private readonly GameObject _root;
    private readonly RawImage _image;
    private readonly AudioSource _music;
    private readonly AudioSource _sound;
    private readonly Text _subtitle;
    private StorySequence[] _subtitles = Array.Empty<StorySequence>();
    private float _start;
    private float _duration;
    private int _generation;
    internal bool IsPlaying => _root && Time.realtimeSinceStartup - _start < _duration;

    internal StoryDialogueMedia(Transform parent, Font font)
    {
        var rect = UiElements.Rect("Story media", parent, 900, 500);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
        rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -40);
        _root = rect.gameObject;
        _image = _root.AddComponent<RawImage>();
        _image.raycastTarget = false;
        _image.enabled = false;
        _music = _root.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _sound = _root.AddComponent<AudioSource>();
        _sound.playOnAwake = false;
        StoryAudio.Configure(_music);
        StoryAudio.Configure(_sound);
        _subtitle = new UiElements(font).Label(parent, "Story subtitles", "", 23, 1050, 90);
        _subtitle.alignment = TextAnchor.MiddleCenter;
        _subtitle.raycastTarget = false;
        _subtitle.rectTransform.anchorMin = _subtitle.rectTransform.anchorMax = new Vector2(.5f, 0);
        _subtitle.rectTransform.anchoredPosition = new Vector2(0, 435);
        _subtitle.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
    }

    internal void Set(StoryPlayback playback)
    {
        Stop();
        _image.texture = playback.Image.Length > 0 ? Load<Texture>(playback.Image, "Image") : null;
        _image.enabled = _image.texture;
        if (_image.texture)
        {
            var ratio = (float)_image.texture!.width / _image.texture.height;
            _image.rectTransform.sizeDelta = ratio > 1.8f ? new Vector2(900, 900 / ratio) : new Vector2(500 * ratio, 500);
        }
        Play(_music, playback.Music, "Audio");
        Play(_sound, playback.Sound, "Audio");
        _subtitles = playback.Subtitles.ToArray();
        _start = Time.realtimeSinceStartup;
        _duration = _subtitles.AsValueEnumerable().Select(static s => s.End).DefaultIfEmpty(0).Max();
        if (_sound.clip && playback.Sound.Length > 0)
        {
            _duration = Math.Max(_duration, _sound.clip!.length);
        }
    }

    internal async UniTask Wait()
    {
        var generation = _generation;
        var cancellation = _root.GetCancellationTokenOnDestroy();
        while (_subtitle && generation == _generation && Time.realtimeSinceStartup - _start < _duration)
        {
            var time = Time.realtimeSinceStartup - _start;
            _subtitle.text = StorySubtitleText.Build(_subtitles, time, static key => Plugin.Localized(key, key), _subtitle.text);
            if (await UniTask.NextFrame(cancellation).SuppressCancellationThrow())
                return;
        }
        if (_subtitle && generation == _generation)
        {
            _subtitle.text = "";
        }
    }

    private void Play(AudioSource source, string id, string kind)
    {
        if (id.Length == 0)
        {
            return;
        }
        source.clip = Load<AudioClip>(id, kind);
        source.Play();
    }

    private T Load<T>(string id, string kind)
        where T : UnityEngine.Object
    {
        var media = StoryMediaStore.Find(id);
        if (media.Kind != kind)
        {
            throw new InvalidDataException("Unexpected story media kind for " + id);
        }
        if (!_bundles.TryGetValue(media.Bundle, out var bundle))
        {
            bundle = StoryMediaStore.Open(media.Bundle, media.Sha256);
            _bundles.Add(media.Bundle, bundle);
        }
        return bundle.LoadAsset<T>(media.Asset) ?? throw new InvalidDataException("The story media asset is missing: " + media.Asset);
    }

    internal void Stop()
    {
        ++_generation;
        _duration = 0;
        if (_subtitle)
            _subtitle.text = "";
        if (_music)
            _music.Stop();
        if (_sound)
            _sound.Stop();
    }

    public void Dispose()
    {
        Stop();
        if (_root)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
        }
        if (_subtitle)
            UnityEngine.Object.Destroy(_subtitle.gameObject);
        foreach (var bundle in _bundles.Values)
        {
            StoryMediaStore.Close(bundle);
        }
        _bundles.Clear();
    }
}
