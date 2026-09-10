using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Media;
using ZLinq;

namespace WTT.Campaigns.Client.Story;

internal sealed class StoryPresentationSurface : IDisposable
{
    internal GameObject Root { get; }
    internal RawImage Background { get; }
    internal RenderTexture Texture { get; }
    private static readonly List<StoryPresentationSurface> Surfaces = new();
    private static readonly List<AudioListener> Listeners = new();
    private static CursorLockMode _cursorLock;
    private static bool _cursorVisible;
    private AudioListener? _listener;
    private readonly GameObject _backdrop;
    private Camera? _camera;
    private bool _disposed;

    internal StoryPresentationSurface(string name, int order)
    {
        if (Surfaces.Count == 0)
        {
            _cursorLock = Cursor.lockState;
            _cursorVisible = Cursor.visible;
        }
        Surfaces.Add(this);
        Root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = Root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = Root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        // Keep the native store covered even before the room produces its first
        // frame or when a recovered shader writes transparent pixels.
        var backdrop = UiElements.Rect("Opaque backdrop", Root.transform, 0, 0);
        UiElements.Stretch(backdrop);
        UiElements.Fill(backdrop, Color.black, true);
        _backdrop = backdrop.gameObject;
        Background = UiElements.Rect("Room", Root.transform, 0, 0).gameObject.AddComponent<RawImage>();
        UiElements.Stretch((RectTransform)Background.transform);
        Background.color = Color.black;
        Background.raycastTarget = true;
        Texture = StoryRoomCamera.CreateTexture(Screen.width, Screen.height);
        Background.texture = Texture;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    internal void UseCamera(Camera camera)
    {
        _camera = camera;
        if (!Surfaces.AsValueEnumerable().Any(s => s._listener))
        {
            foreach (var listener in UnityEngine.Object.FindObjectsOfType<AudioListener>().AsValueEnumerable().Where(l => l.enabled))
            {
                Listeners.Add(listener);
            }
        }
        foreach (var listener in UnityEngine.Object.FindObjectsOfType<AudioListener>().AsValueEnumerable().Where(l => l.enabled))
        {
            listener.enabled = false;
        }
        _listener = camera.GetComponent<AudioListener>() ?? camera.gameObject.AddComponent<AudioListener>();
        _listener.enabled = true;
        camera.targetTexture = Texture;
        var clearColor = camera.backgroundColor;
        clearColor.a = 1;
        camera.backgroundColor = clearColor;
        camera.rect = new Rect(0, 0, 1, 1);
        camera.enabled = true;
        Background.color = Color.white;
    }

    internal void SetBackgroundVisible(bool visible)
    {
        _backdrop.SetActive(visible);
        Background.gameObject.SetActive(visible);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_camera)
        {
            _camera!.enabled = false;
            _camera.targetTexture = null;
        }
        Surfaces.Remove(this);
        if (_listener)
        {
            _listener!.enabled = false;
        }
        UnityEngine.Object.Destroy(Root);
        if (Texture)
        {
            Texture.Release();
            UnityEngine.Object.Destroy(Texture);
        }
        var previous = Surfaces.AsValueEnumerable().LastOrDefault(s => s._listener);
        if (previous != null)
        {
            previous._listener!.enabled = true;
        }
        else
        {
            foreach (var listener in Listeners.AsValueEnumerable().Where(l => l))
            {
                listener.enabled = true;
            }
            Listeners.Clear();
        }
        if (Surfaces.Count == 0)
        {
            Cursor.lockState = _cursorLock;
            Cursor.visible = _cursorVisible;
        }
    }
}
