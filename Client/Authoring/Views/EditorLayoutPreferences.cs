using BepInEx.Configuration;
using Newtonsoft.Json;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Views;

internal static class EditorLayoutPreferences
{
    private static ConfigEntry<string>? _layout;
    private static ConfigEntry<int>? _scale;
    private static ConfigEntry<string>? _sections;
    private static Dictionary<string, bool> _expanded = new();

    internal static bool Expanded(string context, string section, bool defaultValue) =>
        EditorInteractionPolicy.Expanded(_expanded, context, section, defaultValue);

    internal static void SetExpanded(string context, string section, bool value)
    {
        _expanded[context + "/" + section] = value;
        if (_sections == null)
            return;
        try
        {
            _sections.Value = JsonConvert.SerializeObject(_expanded);
            if (!Plugin.Instance.Config.SaveOnConfigSet)
                Plugin.Instance.Config.Save();
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Plugin.Error(error);
        }
    }

    internal static int ScalePercent => _scale?.Value ?? EditorUiScale.DefaultPercent;

    internal static void SetScale(int percent)
    {
        if (_scale == null)
            return;
        try
        {
            _scale.Value = Math.Clamp(percent, EditorUiScale.MinimumPercent, EditorUiScale.MaximumPercent);
            if (!Plugin.Instance.Config.SaveOnConfigSet)
                Plugin.Instance.Config.Save();
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Plugin.Error(error);
        }
    }

    internal static void Attach(EditorToolkitWindows windows)
    {
        _sections ??= Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Inspector sections",
            "",
            "Expanded sections by tool and selection type."
        );
        try
        {
            _expanded = JsonConvert.DeserializeObject<Dictionary<string, bool>>(_sections.Value) ?? new();
        }
        catch (JsonException)
        {
            _expanded = new();
        }
        _scale ??= Plugin.Instance.Config.Bind(
            "Campaign editor",
            "UI size percent",
            EditorUiScale.DefaultPercent,
            new ConfigDescription(
                "Size of in-raid editor controls, text and floating windows. Adjustable from the Windows menu.",
                new AcceptableValueRange<int>(EditorUiScale.MinimumPercent, EditorUiScale.MaximumPercent)
            )
        );
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
