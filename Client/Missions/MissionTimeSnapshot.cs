using EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Restore both the displayed sky and its native clock source, including across midnight.</summary>
internal sealed class MissionTimeSnapshot
{
    private readonly TOD_Time? _time;
    private readonly TOD_CycleParameters? _cycle;
    private readonly GameDateTime? _clock;
    private readonly DateTime _date,
        _gameDate;
    private readonly float _factor,
        _modifier,
        _capturedAt;
    private readonly bool _locked;

    internal MissionTimeSnapshot()
    {
        if (!TODSkyProvider.IsAvailable || !TODSkyProvider.Instance.CurrentTime)
            return;
        _time = TODSkyProvider.Instance.CurrentTime;
        _cycle = TODSkyProvider.Instance.Cycle;
        _clock = _time.GameDateTime;
        _date = _cycle.DateTime;
        _gameDate = _clock?.Calculate() ?? _date;
        _factor = _clock?.TimeFactor ?? 1440f / _time.DayLengthInMinutes;
        _modifier = _clock?.TimeFactorMod ?? 1;
        _locked = _time.LockCurrentTime;
        _capturedAt = Time.realtimeSinceStartup;
    }

    internal void Restore(bool advance = false)
    {
        if (!_time || _cycle == null)
            return;
        if (!TODSkyProvider.IsAvailable || TODSkyProvider.Instance.CurrentTime != _time || _time!.GameDateTime != _clock)
            throw new InvalidOperationException("Checkpoint time belongs to another raid clock.");
        var elapsed = advance ? Math.Max(0, Time.realtimeSinceStartup - _capturedAt) * _factor * _modifier : 0;
        if (_clock != null)
        {
            // Force bypasses the native initialization lock without changing its ownership.
            _clock.ResetForce(_gameDate.AddSeconds(elapsed));
            _clock.TimeFactor = _factor;
            _clock.TimeFactorMod = _modifier;
        }
        _time.LockCurrentTime = _locked;
        _cycle.DateTime = _date.AddSeconds(_locked ? 0 : elapsed);
    }
}
