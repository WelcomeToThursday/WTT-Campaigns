using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Rendering;

// Appended after the native image effects. Copy their finished result without
// taking ownership of SSAA's targetTexture, render rectangle, or quality settings.
internal sealed class EditorViewport : MonoBehaviour
{
    private Camera? _camera;
    private Image? _image;
    private RenderTexture? _texture;
    private Material? _copy;
    private bool _reported;
    private float _savedAspect;
    private bool _automaticAspect;
    private Rect _pixels;
    private Exception? _failure;

    internal void Attach(Camera camera, Image image, Shader shader)
    {
        _camera = camera;
        _image = image;
        _savedAspect = camera.aspect;
        _automaticAspect = Mathf.Approximately(_savedAspect, camera.pixelWidth / (float)Mathf.Max(1, camera.pixelHeight));
        _copy = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
    }

    internal void Resize(Rect pixels)
    {
        if (_failure != null)
            throw new InvalidOperationException("Editor viewport rendering failed.", _failure);
        _pixels = pixels;
        Apply();
    }

    internal void Apply()
    {
        if (!_camera || _pixels.width < 1 || _pixels.height < 1)
            return;
        _camera!.aspect = _pixels.width / _pixels.height;
        SceneViewport.Set(_camera, _pixels);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        try
        {
            if (_failure == null && _image != null && _pixels.width >= 1 && _pixels.height >= 1)
            {
                var width = Mathf.Max(4, Mathf.RoundToInt(_pixels.width));
                var height = Mathf.Max(4, Mathf.RoundToInt(_pixels.height));
                if (!_texture || _texture!.width != width || _texture.height != height)
                {
                    ReleaseTexture();
                    _texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                    {
                        name = "Campaign editor viewport",
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                        useMipMap = false,
                        autoGenerateMips = false,
                    };
                    if (!_texture.Create())
                        throw new InvalidOperationException("Could not allocate the editor viewport.");
                    _image.image = _texture;
                }
                var srgbWrite = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = _texture!.sRGB;
                    Graphics.Blit(source, _texture, _copy);
                }
                finally
                {
                    GL.sRGBWrite = srgbWrite;
                }
                if (!_reported)
                {
                    _reported = true;
                    Plugin.LogInfo(
                        $"Editor viewport: opaque frame {source.width}x{source.height} {source.format} (sRGB={source.sRGB}) -> {width}x{height} (sRGB={_texture!.sRGB}); color space={QualitySettings.activeColorSpace}."
                    );
                }
            }
        }
        catch (Exception error)
        {
            // The owner's next update closes the editor and restores native state.
            _failure = error;
        }
        finally
        {
            // Keep Unity's image-effect chain valid, including on allocation failure.
            Graphics.Blit(source, destination);
        }
    }

    private void ReleaseTexture()
    {
        if (_image != null)
            _image.image = null;
        if (_texture)
        {
            _texture!.Release();
            Destroy(_texture);
        }
        _texture = null;
    }

    private void OnDisable()
    {
        if (!ReferenceEquals(_camera, null))
            SceneViewport.Clear(_camera!);
        if (_camera)
        {
            if (_automaticAspect)
                _camera!.ResetAspect();
            else
                _camera!.aspect = _savedAspect;
        }
        ReleaseTexture();
        if (_copy)
            Destroy(_copy);
        _copy = null;
        _camera = null;
        _image = null;
    }
}
