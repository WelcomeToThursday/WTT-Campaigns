using EFT.Weather;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Client.Missions;

internal sealed class MissionEnvironment : IDisposable
{
    private MissionTimeSnapshot? _before;
    private MissionWeatherOverride? _weather;

    internal MissionEnvironment(MissionEnvironmentSettings? settings)
    {
        var errors = MissionEnvironmentSettings.Errors(settings);
        if (errors.Count != 0)
            throw new InvalidDataException(string.Join("\n", errors));
        // Checkpoint retries also rewind missions that inherit native raid time.
        // Keep their baseline so leaving a rehearsal restores the editor's raid clock.
        _before = new MissionTimeSnapshot();
        try
        {
            if (settings?.StartMinutes is { } minutes && TODSkyProvider.IsAvailable && TODSkyProvider.Instance.CurrentTime)
            {
                var sky = TODSkyProvider.Instance;
                var time = sky.CurrentTime;
                var clock = time.GameDateTime;
                if (clock != null)
                {
                    clock.ResetForce(clock.Calculate().Date.AddMinutes(minutes));
                    if (settings.FreezeTime)
                        clock.TimeFactorMod = 0;
                }
                sky.Cycle.DateTime = sky.Cycle.DateTime.Date.AddMinutes(minutes);
                time.LockCurrentTime = settings.FreezeTime;
            }
            if (settings?.Weather is { } weather && WeatherController.Instance)
                _weather = new MissionWeatherOverride(WeatherController.Instance, MissionWeatherOverride.Create(weather));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _weather?.Dispose();
        _weather = null;
        try
        {
            _before?.Restore(advance: true);
        }
        catch (Exception error)
        {
            Plugin.Error(error);
        }
        _before = null;
    }
}
