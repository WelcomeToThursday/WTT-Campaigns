using BepInEx.Configuration;
using EFT.Weather;
using Newtonsoft.Json;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Client.Authoring.Rendering;

internal sealed partial class EditorEnvironment
{
    private static ConfigEntry<string>? _preferencesEntry;
    private MissionEnvironmentSettings _preferences = new();
    private bool _pendingTime,
        _pendingWeather;

    private void LoadPreferences()
    {
        _preferencesEntry ??= Plugin.Instance.Config.Bind(
            "Campaign editor",
            "Time and weather",
            "",
            "Remembered editor time and weather. Mission conditions are configured separately in the server web editor."
        );
        try
        {
            _preferences = JsonConvert.DeserializeObject<MissionEnvironmentSettings>(_preferencesEntry.Value) ?? new();
            if (MissionEnvironmentSettings.Errors(_preferences).Count > 0)
                _preferences = new();
        }
        catch (JsonException)
        {
            _preferences = new();
        }
        _pendingTime = _preferences.StartMinutes != null;
        _pendingWeather = _preferences.Weather != null;
    }

    private void ApplyPreferences()
    {
        // Some maps attach their sky/weather after the editor camera is ready.
        if (_pendingTime && Available)
        {
            SetHour(_preferences.StartMinutes!.Value / 60f);
            _pendingTime = false;
        }
        if (_pendingWeather && WeatherAvailable)
        {
            var weather = _preferences.Weather!;
            SetWeather(weather.Clouds, weather.Rain, weather.Fog, weather.Wind, weather.Thunder);
            _previewWeather!.WindDirection = (WeatherDebug.Direction)weather.Direction;
            _pendingWeather = false;
        }
    }

    internal void RememberTime()
    {
        _pendingTime = false;
        _preferences.StartMinutes = Previewing ? (int)Math.Round(Hour * 60) % 1440 : null;
        SavePreferences();
    }

    internal void RememberWeather()
    {
        _pendingWeather = false;
        _preferences.Weather =
            _previewWeather == null
                ? null
                : new MissionWeatherSettings
                {
                    Clouds = Math.Clamp((_previewWeather.CloudDensity + 1) * 50, 0, 100),
                    Rain = Math.Clamp(_previewWeather.Rain * 100, 0, 100),
                    Fog = Math.Clamp((_previewWeather.Fog - .001f) / .254f * 100, 0, 100),
                    Wind = Math.Clamp(_previewWeather.WindMagnitude * 100, 0, 100),
                    Thunder = Math.Clamp(_previewWeather.LightningThunderProbability * 100, 0, 100),
                    Direction = (int)_previewWeather.WindDirection,
                };
        SavePreferences();
    }

    private void SavePreferences()
    {
        _preferencesEntry!.Value = JsonConvert.SerializeObject(_preferences);
        if (!Plugin.Instance.Config.SaveOnConfigSet)
            Plugin.Instance.Config.Save();
    }
}
