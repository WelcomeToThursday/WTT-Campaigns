using EFT.Weather;
using HarmonyLib;
using UnityEngine;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Scoped curve overlays; editor previews can temporarily cover mission weather.</summary>
internal sealed class MissionWeatherOverride : IDisposable
{
    private static readonly List<MissionWeatherOverride> Active = new();
    private static bool _patched;
    private readonly WeatherController _controller;
    internal WeatherDebug Curve { get; }

    internal MissionWeatherOverride(WeatherController controller, WeatherDebug curve)
    {
        if (!_patched)
        {
            new Harmony("com.wtt.campaigns.weather").Patch(
                AccessTools.PropertyGetter(typeof(WeatherController), nameof(WeatherController.WeatherCurve)),
                prefix: new HarmonyMethod(typeof(MissionWeatherOverride), nameof(GetCurve))
            );
            _patched = true;
        }
        _controller = controller;
        Curve = curve;
        Active.Add(this);
    }

    internal static WeatherDebug Create(MissionWeatherSettings settings) =>
        new()
        {
            Enabled = true,
            IsDynamicSunWeatherDebug = false,
            CloudDensity = Mathf.Lerp(-1, 1, settings.Clouds / 100),
            Rain = settings.Rain / 100,
            Fog = Mathf.Lerp(.001f, .255f, settings.Fog / 100),
            WindMagnitude = settings.Wind / 100,
            LightningThunderProbability = settings.Thunder / 100,
            WindDirection = (WeatherDebug.Direction)settings.Direction,
        };

    private static bool GetCurve(WeatherController __instance, ref IWeatherCurve __result)
    {
        for (var i = Active.Count - 1; i >= 0; i--)
            if (Active[i]._controller == __instance)
            {
                __result = Active[i].Curve;
                return false;
            }
        return true;
    }

    public void Dispose() => Active.Remove(this);
}
