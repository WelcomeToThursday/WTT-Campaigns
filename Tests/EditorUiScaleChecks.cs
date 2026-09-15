using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Tests;

internal static class EditorUiScaleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var normal = EditorUiScale.Resolve(1920, 1080, 85);
        check(Math.Abs(normal - .85f) < .001f, "Editor defaults to compact controls at 1080p");
        var small = EditorUiScale.Resolve(1280, 720, 85);
        check(Math.Abs(1280 / small - 1920 / normal) < .01f, "720p retains the same logical workspace instead of oversized controls");
        check(Math.Abs(EditorUiScale.Resolve(3840, 2160, 85) - normal * 2) < .001f, "4K scales text and controls together");
        check(EditorUiScale.Resolve(1280, 1080, 85) == small, "Narrow windows fit their width");
        check(EditorUiScale.Resolve(3440, 1440, 85) == EditorUiScale.Resolve(2560, 1440, 85), "Ultrawide displays retain extra workspace");
        check(EditorUiScale.Resolve(1920, 1080, 60) < normal, "User can reduce control size further");
        check(EditorUiScale.Resolve(1920, 1080, 0) == .6f, "Invalid low preferences retain a usable size");
        check(EditorUiScale.Resolve(1920, 1080, 500) == 1.3f, "Invalid high preferences are bounded");
        check(EditorUiScale.Resolve(0, 0, 85) > 0, "Minimized viewport never divides by zero");
    }
}
