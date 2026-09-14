using System.Globalization;
using UnityEngine.UI;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private EditorEnvironment? _environment;
    private string _environmentError = "";
    private string _weatherError = "";

    private void BindEnvironment(RaidEditorView view)
    {
        void Button(string name, Action action) =>
            view.Button(
                name,
                () =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        Plugin.Error(e);
                        _notice = e.Message;
                    }
                }
            );
        Button(
            "EnvironmentToggle",
            () =>
            {
                RefreshEnvironment();
                RefreshWeather();
            }
        );
        void Set(float hour)
        {
            _environment?.SetHour(hour);
            _environmentError = "";
            RefreshEnvironment();
        }
        Button("EnvironmentDawn", () => Set(6));
        Button("EnvironmentNoon", () => Set(12));
        Button("EnvironmentDusk", () => Set(18));
        Button("EnvironmentNight", () => Set(0));
        Button("EnvironmentEarlier", () => Set((_environment?.Hour ?? 0) - 1));
        Button("EnvironmentLater", () => Set((_environment?.Hour ?? 0) + 1));
        Button(
            "EnvironmentApply",
            () =>
            {
                var text = view.Get<InputField>("EnvironmentHour").text.Trim();
                if (EnvironmentValues.TryHour(text, out var hour))
                    Set(hour);
                else
                {
                    _environmentError = "Enter a time from 00:00 to 23:59.";
                    RefreshEnvironment();
                }
            }
        );
        Button(
            "EnvironmentReset",
            () =>
            {
                _environment?.RestoreTime();
                _environmentError = "";
                RefreshEnvironment();
            }
        );
        void Weather(float clouds, float rain, float fog, float wind, float thunder)
        {
            _environment?.SetWeather(clouds, rain, fog, wind, thunder);
            _weatherError = "";
            RefreshWeather();
        }
        Button("WeatherPresetClear", () => Weather(0, 0, 0, 10, 0));
        Button("WeatherPresetCloudy", () => Weather(80, 0, 5, 30, 0));
        Button("WeatherPresetRain", () => Weather(100, 60, 15, 40, 0));
        Button("WeatherPresetStorm", () => Weather(100, 100, 30, 80, 75));
        Button(
            "WeatherApply",
            () =>
            {
                var names = new[] { "Clouds", "Rain", "Fog", "Wind", "Thunder" };
                var values = new float[names.Length];
                for (var i = 0; i < names.Length; i++)
                    if (!EnvironmentValues.TryPercent(view.Get<InputField>("Weather" + names[i]).text, out values[i]))
                    {
                        _weatherError = names[i] + " must be a number from 0 to 100.";
                        RefreshWeather(false);
                        return;
                    }
                Weather(values[0], values[1], values[2], values[3], values[4]);
            }
        );
        Button(
            "WeatherDirection",
            () =>
            {
                _environment?.CycleWind();
                RefreshWeather(false);
            }
        );
        Button(
            "WeatherReset",
            () =>
            {
                _environment?.RestoreWeather();
                _weatherError = "";
                RefreshWeather();
            }
        );
    }

    private void RefreshEnvironment()
    {
        if (_view?.Valid != true)
            return;
        var available = _environment?.Available == true;
        foreach (var name in new[] { "Apply", "Dawn", "Noon", "Dusk", "Night", "Earlier", "Later", "Reset" })
            _view.Get<Button>("Environment" + name).interactable = available;
        _view.Get<InputField>("EnvironmentHour").interactable = available;
        // Keep an unsubmitted edit until Set time or a preset is chosen.
        if (_environmentError.Length == 0 && !_view.Get<InputField>("EnvironmentHour").isFocused)
            _view.Value("EnvironmentHour", TimeSpan.FromMinutes(Math.Round((_environment?.Hour ?? 0) * 60) % 1440).ToString(@"hh\:mm"));
        _view.Text(
            "EnvironmentStatus",
            _environmentError.Length > 0 ? _environmentError
                : !available ? "This map has no adjustable sky clock."
                : _environment!.Previewing ? "Preview time held. Closing restores raid time."
                : "Using raid time. Visibility follows the free camera."
        );
    }

    private void RefreshWeather(bool values = true)
    {
        if (_view?.Valid != true)
            return;
        var available = _environment?.WeatherAvailable == true;
        foreach (var name in new[] { "PresetClear", "PresetCloudy", "PresetRain", "PresetStorm", "Apply", "Reset", "Direction" })
            _view.Get<Button>("Weather" + name).interactable = available;
        var names = new[] { "Clouds", "Rain", "Fog", "Wind", "Thunder" };
        foreach (var name in names)
            _view.Get<InputField>("Weather" + name).interactable = available;
        var weather = _environment?.Weather;
        if (values && weather != null && _weatherError.Length == 0)
        {
            var numbers = new[]
            {
                (weather.Cloudiness + 1) * 50,
                weather.Rain * 100,
                (weather.Fog - .001f) / .254f * 100,
                weather.Wind.magnitude * 100,
                weather.LightningThunderProbability * 100,
            };
            for (var i = 0; i < names.Length; i++)
                _view.Value("Weather" + names[i], UnityEngine.Mathf.Clamp(numbers[i], 0, 100).ToString("0", CultureInfo.InvariantCulture));
        }
        _view.Caption("WeatherDirection", "Wind: " + (_environment?.WindDirection ?? "Raid"));
        _view.Text(
            "WeatherStatus",
            _weatherError.Length > 0 ? _weatherError
                : !available ? "This map has no weather controller."
                : _environment!.WeatherPreviewing ? "Weather preview active. Closing restores raid weather."
                : "Using raid weather. Values are percentages."
        );
    }
}
