using BepInEx.Configuration;

namespace WTT.Campaigns.Client.Authoring.Views;

internal static class EditorConsolePreferences
{
    internal const int DefaultTextSize = 16,
        MinimumTextSize = 10,
        MaximumTextSize = 24;
    private static ConfigEntry<int>? _textSize;

    private static ConfigEntry<int> Setting =>
        _textSize ??= Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Console text size",
            DefaultTextSize,
            new ConfigDescription(
                "Console output, details and command text size before the editor UI scale is applied.",
                new AcceptableValueRange<int>(MinimumTextSize, MaximumTextSize)
            )
        );

    internal static int TextSize => Math.Clamp(Setting.Value, MinimumTextSize, MaximumTextSize);

    internal static void SetTextSize(int value)
    {
        try
        {
            Setting.Value = Math.Clamp(value, MinimumTextSize, MaximumTextSize);
            if (!Plugin.Instance.Config.SaveOnConfigSet)
                Plugin.Instance.Config.Save();
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Plugin.Error(error);
        }
    }
}
