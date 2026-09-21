using Comfort.Common;
using EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Missions;

/// <summary>One mission owns the native deadline; rehearsals never end the editor raid.</summary>
internal sealed class MissionRaidTimer : IDisposable
{
    internal static MissionRaidTimer? Current { get; private set; }
    private readonly AbstractGame _game;
    private readonly GameTimer _native;
    private readonly TimeSpan? _originalSession;
    private readonly DateTime? _originalEscape;
    private readonly bool _rehearsal;
    private readonly MissionCountdown _countdown;
    private bool _disposed;
    internal double? Remaining => _countdown.Remaining(Time.realtimeSinceStartup);
    internal bool Expired => !_countdown.Paused && Remaining is <= 0;

    internal MissionRaidTimer(int? minutes, bool rehearsal)
    {
        if (minutes is < 0 or > 1440)
            throw new InvalidDataException("Invalid server mission time limit.");
        if (Current != null)
            throw new InvalidOperationException("Another mission owns the raid timer.");
        _game = Singleton<AbstractGame>.Instance ?? throw new InvalidOperationException("Native raid timer unavailable.");
        _native = _game.GameTimer;
        _originalSession = _native.SessionTime;
        _originalEscape = _native.EscapeDateTime;
        _rehearsal = rehearsal;
        double? seconds = minutes.HasValue
            ? minutes == 0
                ? null
                : minutes.Value * 60d
            : rehearsal
                ? _originalSession?.TotalSeconds
                : _originalEscape.HasValue
                    ? Math.Max(0, (_originalEscape.Value - DateTimeExtensions.UtcNow).TotalSeconds)
                    : _originalSession?.TotalSeconds;
        _countdown = new MissionCountdown(seconds);
        Current = this;
        Apply();
    }

    private bool OwnsTimer =>
        !_disposed
        && Singleton<AbstractGame>.Instantiated
        && ReferenceEquals(Singleton<AbstractGame>.Instance, _game)
        && ReferenceEquals(_game.GameTimer, _native);

    private void Apply()
    {
        if (!OwnsTimer)
            return;
        // Null is the native no-expiry state, not an arbitrarily large duration.
        if (_rehearsal || _countdown.Paused || !Remaining.HasValue)
        {
            _native.SessionTime = null;
            _native._escapeDateTime = null;
        }
        else
            _native.ChangeSessionTime(_native.PastTime + TimeSpan.FromSeconds(Remaining!.Value));
    }

    internal void Pause()
    {
        _countdown.Pause(Time.realtimeSinceStartup);
        Apply();
    }

    internal void Resume()
    {
        _countdown.Resume(Time.realtimeSinceStartup);
        Apply();
    }

    internal Snapshot Capture() => new(this, Remaining);

    internal sealed class Snapshot
    {
        private readonly MissionRaidTimer _owner;
        private readonly double? _seconds;

        internal Snapshot(MissionRaidTimer owner, double? seconds) => (_owner, _seconds) = (owner, seconds);

        internal void Restore()
        {
            if (!ReferenceEquals(Current, _owner) || !_owner.OwnsTimer)
                throw new InvalidOperationException("Checkpoint timer belongs to another mission.");
            _owner._countdown.Restore(_seconds, Time.realtimeSinceStartup);
            _owner.Apply();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (OwnsTimer)
        {
            _native.SessionTime = _originalSession;
            _native._escapeDateTime = _originalEscape;
        }
        _disposed = true;
        if (ReferenceEquals(Current, this))
            Current = null;
    }
}
