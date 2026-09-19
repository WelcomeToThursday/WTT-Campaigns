using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class EditorViewportInteractionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var options = new EditorViewportState { Overlays = EditorOverlays.Zones | EditorOverlays.Handles };
        check(options.Shows(EditorOverlays.Zones) && !options.Shows(EditorOverlays.Ai), "Viewport overlays are independent.");
        options.Clean = true;
        check(!options.Shows(EditorOverlays.All), "Clean view suppresses all overlay drawing and handle hits.");
        options.Clean = false;
        check(
            options.Shows(EditorOverlays.Handles) && !options.Shows(EditorOverlays.Routes),
            "Leaving clean view restores the previous overlay choices."
        );
        var dock = new EditorDockRect(200, 100, 800, 600);
        var content = EditorViewportState.Content(dock);
        check(
            content.X == 200 && content.Y == 130 && content.Width == 800 && content.Height == 570,
            "Camera image and pointer mapping exclude the fixed viewport toolbar."
        );
        var pixels = EditorViewportCoordinates.Pixels(content, 1, 1920, 1080);
        check(!pixels.Contains(600, 960) && pixels.Contains(600, 900), "Toolbar clicks do not enter the camera viewport.");
        foreach (var gesture in new[] { EditorCameraGesture.Fly, EditorCameraGesture.Pan, EditorCameraGesture.Orbit })
        {
            var right = gesture == EditorCameraGesture.Fly;
            var middle = gesture == EditorCameraGesture.Pan;
            var left = gesture == EditorCameraGesture.Orbit;
            var resolve = (EditorCameraGesture current, bool allowed, bool inside) =>
                EditorCameraGesturePolicy.Resolve(current, allowed, inside, right, middle, left, left);
            check(resolve(EditorCameraGesture.None, true, false) == EditorCameraGesture.None, "Camera gestures cannot start over a panel.");
            check(
                resolve(EditorCameraGesture.None, true, true) == gesture,
                "Viewport mouse modifiers choose the requested navigation gesture."
            );
            check(resolve(gesture, true, false) == gesture, "A captured camera gesture survives crossing a panel or the monitor center.");
            check(resolve(gesture, false, true) == EditorCameraGesture.None, "Focus loss, text entry and modals cancel camera capture.");
            check(
                EditorCameraGesturePolicy.Resolve(gesture, true, true, false, false, false, false) == EditorCameraGesture.None,
                "Releasing the gesture button releases capture."
            );
        }
        check(
            EditorCameraGesturePolicy.Resolve(EditorCameraGesture.None, true, true, false, false, true, false) == EditorCameraGesture.None,
            "Ordinary scene selection never starts orbit."
        );
        var pointer = new EditorPointerRestore();
        pointer.Remember(42, -1700, 410);
        pointer.Remember(42, 960, 540);
        check(
            pointer.Release(42, true, out var x, out var y) && x == -1700 && y == 410,
            "Release restores the pre-lock pointer, including negative coordinates on another monitor."
        );
        check(!pointer.Release(42, true, out _, out _), "Pointer restoration happens once, not every idle frame.");
        pointer.Remember(42, 120, 300);
        check(
            !pointer.Release(43, true, out _, out _) && !pointer.Pending,
            "Switching applications discards restoration without moving another app's cursor."
        );
        pointer.Remember(42, 120, 300);
        check(!pointer.Release(42, false, out _, out _) && !pointer.Pending, "Losing focus cannot leave a delayed cursor warp.");
    }
}
