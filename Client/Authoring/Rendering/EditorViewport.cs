using UnityEngine;
using UnityEngine.Rendering;
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
    private CommandBuffer? _navigationCommands;
    private bool _reported;
    private bool _navigationReported;
    private DepthTextureMode _savedDepthMode;
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
        _savedDepthMode = camera.depthTextureMode;
        camera.depthTextureMode |= DepthTextureMode.Depth;
        _navigationCommands = new CommandBuffer { name = "Campaign editor navigation layer" };
        camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _navigationCommands);
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

    private void OnPreRender()
    {
        if (!_camera || !_copy || _navigationCommands == null)
            return;
        _navigationCommands.Clear();
        if (!Navigation.NavigationSurfaceRenderer.ProjectOntoGround || _pixels.width < 1 || _pixels.height < 1)
            return;
        try
        {
            // Draw into the active native camera target. Coverage and scene colour
            // must share SSAA/post-processing orientation, jitter and scaling; a
            // separate layer composited after those effects can float over the scene.
            var triangles = Navigation.NavigationSurfaceRenderer.RecordOverlay(_camera!, _navigationCommands);
            if (triangles > 0 && !_navigationReported)
            {
                _navigationReported = true;
                Plugin.LogInfo(
                    $"Navigation renderer: native-frame ground projection v2; {triangles} source triangles; camera={_camera.name}; path={_camera.actualRenderingPath}; client={typeof(EditorViewport).Assembly.ManifestModule.ModuleVersionId}."
                );
            }
        }
        catch (Exception error)
        {
            _navigationCommands.Clear();
            _failure = error;
        }
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
        if (_navigationCommands != null)
        {
            if (_camera)
                _camera!.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _navigationCommands);
            _navigationCommands.Release();
            _navigationCommands = null;
        }
        if (!ReferenceEquals(_camera, null))
            SceneViewport.Clear(_camera!);
        if (_camera)
        {
            // Preserve any other depth modes requested while the editor was open.
            _camera!.depthTextureMode = (_camera.depthTextureMode & ~DepthTextureMode.Depth) | _savedDepthMode;
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
