using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

public static partial class RaidEditorLayout
{
    // Also upgrades an already-built native prefab before its controls are bound.
    public static void EnsureEnvironment(GameObject root, UiElements ui)
    {
        // Environment may already be a floating tool instead of a menu child.
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == "EnvironmentMenu")
                return;
        var menus = root.transform.Find("MenuLayer");
        var session = (RectTransform)menus.Find("ContextMenu");
        session.sizeDelta = new Vector2(310, 106);
        session.anchoredPosition = new Vector2(-167, -97);
        MenuButton(ui, session, "EnvironmentToggle", "Environment / time and weather", 54);
        var panel = Menu(ui, menus, "EnvironmentMenu", 340, 636);
        panel.GetComponent<Image>().color = new Color(.065f, .07f, .065f, 1);
        Place(ui.Label(panel, "EnvironmentTitle", "TIME OF DAY", 20, 312, 30).rectTransform, 14, 8, 312, 30);
        Place(ui.Input(panel, "EnvironmentHour", "HH:mm", 142, 0, 0).GetComponent<RectTransform>(), 14, 48, 142, 36);
        StripButton(ui, panel, "EnvironmentApply", "Set time", 154, 170);
        Place((RectTransform)panel.Find("EnvironmentApply"), 170, 48, 154, 36);
        var names = new[] { "Dawn", "Noon", "Dusk", "Night" };
        for (var i = 0; i < names.Length; i++)
        {
            Button(ui, panel, "Environment" + names[i], names[i], 72, 0, 0, 34);
            Place((RectTransform)panel.Find("Environment" + names[i]), 14 + i * 80, 94, 72, 34);
        }
        Button(ui, panel, "EnvironmentEarlier", "-1 hour", 150, 0, 0, 34);
        Place((RectTransform)panel.Find("EnvironmentEarlier"), 14, 138, 150, 34);
        Button(ui, panel, "EnvironmentLater", "+1 hour", 150, 0, 0, 34);
        Place((RectTransform)panel.Find("EnvironmentLater"), 174, 138, 150, 34);
        MenuButton(ui, panel, "EnvironmentReset", "Use raid time", 182);
        Place(ui.Label(panel, "EnvironmentStatus", "", 15, 312, 56).rectTransform, 14, 226, 312, 56);
        Place(ui.Label(panel, "WeatherHeading", "WEATHER", 20, 312, 30).rectTransform, 14, 282, 312, 30);
        var presets = new[] { "Clear", "Cloudy", "Rain", "Storm" };
        for (var i = 0; i < presets.Length; i++)
        {
            Button(ui, panel, "WeatherPreset" + presets[i], presets[i], 72, 0, 0, 34);
            Place((RectTransform)panel.Find("WeatherPreset" + presets[i]), 14 + i * 80, 318, 72, 34);
        }
        var fields = new[] { "Clouds", "Rain", "Fog", "Wind", "Thunder" };
        for (var i = 0; i < fields.Length; i++)
        {
            var x = 14 + i % 2 * 160;
            var y = 360 + i / 2 * 50;
            Place(ui.Label(panel, "Weather" + fields[i] + "Label", fields[i] + " %", 14, 150, 18).rectTransform, x, y, 150, 18);
            Place(ui.Input(panel, "Weather" + fields[i], "0–100", 150, 0, 0).GetComponent<RectTransform>(), x, y + 18, 150, 30);
        }
        Button(ui, panel, "WeatherDirection", "Wind: raid", 150, 0, 0, 30);
        Place((RectTransform)panel.Find("WeatherDirection"), 174, 478, 150, 30);
        Button(ui, panel, "WeatherApply", "Apply weather", 150, 0, 0, 36);
        Place((RectTransform)panel.Find("WeatherApply"), 14, 520, 150, 36);
        Button(ui, panel, "WeatherReset", "Use raid weather", 150, 0, 0, 36);
        Place((RectTransform)panel.Find("WeatherReset"), 174, 520, 150, 36);
        Place(ui.Label(panel, "WeatherStatus", "", 15, 312, 64).rectTransform, 14, 566, 312, 64);
    }
}
