using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>Optional server-authored environment. Missing overrides retain native raid conditions.</summary>
public sealed class MissionEnvironmentSettings : ExtensibleJsonModel
{
    public int? StartMinutes { get; set; }
    public bool FreezeTime { get; set; }
    public MissionWeatherSettings? Weather { get; set; }

    public static List<string> Errors(MissionEnvironmentSettings? settings)
    {
        var errors = new List<string>();
        if (settings == null)
            return errors;
        if (settings.StartMinutes is < 0 or > 1439)
            errors.Add("Mission start time must be between 00:00 and 23:59.");
        if (settings.FreezeTime && settings.StartMinutes == null)
            errors.Add("Choose a mission start time before freezing time.");
        if (settings.Weather is { } weather)
        {
            foreach (
                var (name, value) in new[]
                {
                    ("Clouds", weather.Clouds),
                    ("Rain", weather.Rain),
                    ("Fog", weather.Fog),
                    ("Wind", weather.Wind),
                    ("Thunder", weather.Thunder),
                }
            )
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 100)
                    errors.Add(name + " must be a finite percentage from 0 to 100.");
            if (weather.Direction is < 1 or > 8)
                errors.Add("Choose one of the eight wind directions.");
        }
        return errors;
    }
}

public sealed class MissionWeatherSettings : ExtensibleJsonModel
{
    public float Clouds { get; set; }
    public float Rain { get; set; }
    public float Fog { get; set; }
    public float Wind { get; set; } = 10;
    public float Thunder { get; set; }
    public int Direction { get; set; } = 1;
}
