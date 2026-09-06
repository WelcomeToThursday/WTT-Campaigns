using Comfort.Common;
using EFT.UI;
using UnityEngine;

namespace SeasonalPerks.Client.Hub;

public sealed class HubBannerSound : MonoBehaviour
{
    private AudioSource? _source;
    private bool _hover;

    internal void Initialize(AudioClip clip)
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.clip = clip;
        _source.loop = true;
        _source.playOnAwake = false;
        _source.spatialBlend = 0;
        _source.volume = 0;
    }

    internal void Hover(bool value)
    {
        _hover = value;
        if (!value || !_source || !Singleton<GUISounds>.Instantiated)
        {
            return;
        }

        _source!.outputAudioMixerGroup = Singleton<GUISounds>.Instance.MasterMixer.FindMatchingGroups("UI").FirstOrDefault();
        if (!_source.isPlaying)
        {
            _source.Play();
        }
    }

    private void Update()
    {
        if (!_source)
        {
            return;
        }

        _source!.volume = Mathf.MoveTowards(_source.volume, _hover ? .3f : 0, Time.unscaledDeltaTime * (_hover ? 3 : 1));
        if (!_hover && _source.volume == 0)
        {
            _source.Stop();
        }
    }

    private void OnDisable()
    {
        _hover = false;
        if (!_source)
        {
            return;
        }

        _source!.Stop();
        _source.volume = 0;
    }
}
