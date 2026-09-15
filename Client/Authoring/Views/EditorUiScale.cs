namespace WTT.Campaigns.Client.Authoring.Views;

internal static class EditorUiScale
{
    internal const int DefaultPercent = 85;
    internal const int MinimumPercent = 60;
    internal const int MaximumPercent = 130;

    // Fit both dimensions; a pixel-size floor prevents small windows from fitting.
    internal static float Resolve(int width, int height, int percent) =>
        Math.Min(Math.Max(1, width) / 1920f, Math.Max(1, height) / 1080f)
        * Math.Clamp(percent, MinimumPercent, MaximumPercent) / 100f;
}
