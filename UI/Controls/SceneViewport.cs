using UnityEngine;

namespace WTT.Campaigns.UI.Controls;

// The editor presents the final native frame in a panel. Never use the native
// camera's pixelRect for editor input: SSAA owns that rectangle and may resize it.
public static class SceneViewport
{
    private static Camera? _camera;
    private static Rect _pixels;

    public static void Set(Camera camera, Rect pixels)
    {
        _camera = camera;
        _pixels = pixels;
    }

    public static void Clear(Camera camera)
    {
        if (_camera == camera)
            _camera = null;
    }

    public static Rect ScreenRect(Camera camera) => _camera == camera ? _pixels : camera.pixelRect;

    public static Vector3 EditorWorldToScreenPoint(this Camera camera, Vector3 world)
    {
        if (_camera != camera)
            return camera.WorldToScreenPoint(world);
        var point = camera.WorldToViewportPoint(world);
        var screen = EditorViewportCoordinates.Project(new(_pixels.x, _pixels.y, _pixels.width, _pixels.height), point.x, point.y);
        return new(screen.X, screen.Y, point.z);
    }

    public static Ray EditorScreenPointToRay(this Camera camera, Vector3 screen)
    {
        if (_camera != camera)
            return camera.ScreenPointToRay(screen);
        var point = EditorViewportCoordinates.Normalize(new(_pixels.x, _pixels.y, _pixels.width, _pixels.height), screen.x, screen.y);
        return camera.ViewportPointToRay(new Vector3(point.X, point.Y, screen.z));
    }
}
