using BepInEx.Configuration;
using Newtonsoft.Json;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring;

internal static class EditorLayoutPreferences
{
    private static ConfigEntry<string>? _layout;

    internal static void Attach(EditorToolkitWindows windows)
    {
        _layout ??= Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Tool window layout",
            "",
            "Local editor window positions and sizes. Use Windows / Reset layout to restore defaults."
        );
        try
        {
            if (!string.IsNullOrWhiteSpace(_layout.Value))
                windows.RestoreLayout(JsonConvert.DeserializeObject<EditorWindowLayout>(_layout.Value));
        }
        catch (JsonException)
        {
            Plugin.LogInfo("Editor window preferences were invalid; using the default layout.");
        }
        windows.LayoutChanged = () => Save(windows);
    }

    internal static void Save(EditorToolkitWindows windows)
    {
        if (_layout == null)
            return;
        var value = JsonConvert.SerializeObject(windows.CaptureLayout());
        if (_layout.Value == value)
            return;
        try
        {
            _layout.Value = value;
            if (!Plugin.Instance.Config.SaveOnConfigSet)
                Plugin.Instance.Config.Save();
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            // A read-only configuration must not interrupt window input or view teardown.
            Plugin.Error(error);
        }
    }
}
