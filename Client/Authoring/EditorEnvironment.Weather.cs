using EFT.Weather;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

internal sealed partial class EditorEnvironment
{
    private WeatherController? _weather;
    private WeatherDebug? _previewWeather;
    internal bool WeatherAvailable =>
        WeatherController.Instance && WeatherController.Instance.WeatherDebug != null && WeatherController.Instance.WeatherCurve != null;
    internal bool WeatherPreviewing => _weather && _weather == WeatherController.Instance && _previewWeather != null;
    internal IWeatherCurve? Weather => WeatherAvailable ? WeatherController.Instance.WeatherCurve : null;
    internal string WindDirection => WeatherPreviewing ? _previewWeather!.WindDirection.ToString() : "Raid";

    private WeatherDebug? BeginWeather()
    {
        if (!WeatherAvailable)
            return null;
        var controller = WeatherController.Instance;
        if (_weather != controller || _previewWeather == null)
        {
            RestoreWeather();
            var source = controller.WeatherCurve;
            // Overlay only the weather curve. Native backend updates and debug settings
            // continue untouched, so removing the overlay reveals current raid weather.
            var preview = new WeatherDebug();
            preview.CopyParams(source);
            preview.LightningThunderProbability = source.LightningThunderProbability;
            var closest = float.NegativeInfinity;
            foreach (WeatherDebug.Direction direction in Enum.GetValues(typeof(WeatherDebug.Direction)))
            {
                var alignment = Vector2.Dot(WeatherNode.WindDirections[(int)direction].normalized, source.Wind.normalized);
                if (alignment > closest)
                {
                    closest = alignment;
                    preview.WindDirection = direction;
                }
            }
            preview.Enabled = true;
            preview.IsDynamicSunWeatherDebug = false;
            _weather = controller;
            _previewWeather = preview;
        }
        return _previewWeather;
    }

    internal void SetWeather(float clouds, float rain, float fog, float wind, float thunder)
    {
        var preview = BeginWeather();
        if (preview == null)
            return;
        preview.CloudDensity = Mathf.Lerp(-1, 1, clouds / 100);
        preview.Rain = Mathf.Clamp01(rain / 100);
        preview.Fog = Mathf.Lerp(.001f, .255f, fog / 100);
        preview.WindMagnitude = Mathf.Clamp01(wind / 100);
        preview.LightningThunderProbability = Mathf.Clamp01(thunder / 100);
    }

    internal void CycleWind()
    {
        var preview = BeginWeather();
        if (preview != null)
            preview.WindDirection = (WeatherDebug.Direction)((int)preview.WindDirection % 8 + 1);
    }

    internal void RestoreWeather()
    {
        _weather = null;
        _previewWeather = null;
    }

    private static bool WeatherCurve(WeatherController __instance, ref IWeatherCurve __result)
    {
        if (_active == null || _active._weather != __instance || _active._previewWeather == null)
            return true;
        __result = _active._previewWeather;
        return false;
    }
}
