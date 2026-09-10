using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace WTT.Campaigns.Client.Hub;

public sealed class HubVideo : MonoBehaviour
{
    private VideoPlayer? _player;
    private RawImage? _image;
    private Image? _fallback;

    internal void Initialize(VideoClip clip, RawImage image, bool loop, Image? fallback)
    {
        _image = image;
        _fallback = fallback;
        _player = gameObject.AddComponent<VideoPlayer>();
        _player.playOnAwake = false;
        _player.clip = clip;
        _player.isLooping = loop;
        _player.audioOutputMode = VideoAudioOutputMode.None;
        _player.renderMode = VideoRenderMode.APIOnly;
        _player.sendFrameReadyEvents = true;
        _player.prepareCompleted += Prepared;
        _player.frameReady += FrameReady;
        _player.errorReceived += Failed;
        _player.Prepare();
    }

    private void Prepared(VideoPlayer player)
    {
        if (!isActiveAndEnabled || !_image)
        {
            return;
        }

        player.Play();
    }

    private void FrameReady(VideoPlayer player, long frame)
    {
        if (!isActiveAndEnabled || !_image || !player.texture || _image!.texture == player.texture)
        {
            return;
        }

        _image.texture = player.texture;
        _image.color = Color.white;
        if (_fallback)
        {
            _fallback!.enabled = false;
        }
        Plugin.LogInfo("Season hub video playing: " + player.clip.name);
    }

    private void Failed(VideoPlayer player, string message)
    {
        if (_fallback)
        {
            _fallback!.enabled = true;
        }
        if (_image)
        {
            _image!.color = Color.clear;
        }

        Plugin.LogInfo("Season hub video unavailable: " + message);
    }

    private void OnEnable()
    {
        if (_player)
        {
            _player!.Prepare();
        }
    }

    private void OnDisable()
    {
        if (_fallback)
        {
            _fallback!.enabled = true;
        }
        if (_player)
        {
            _player!.Stop();
        }

        if (_image)
        {
            _image!.texture = null;
            _image.color = Color.clear;
        }
    }

    private void OnDestroy()
    {
        if (!_player)
        {
            return;
        }

        _player!.prepareCompleted -= Prepared;
        _player.frameReady -= FrameReady;
        _player.errorReceived -= Failed;
        _player.Stop();
    }
}
