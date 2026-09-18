using EFT;
using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Collects native observations; only accepted server state dispatches mission actions.</summary>
internal sealed class MissionDirector : IDisposable
{
    private readonly MapLayout _layout;
    private readonly Player _player;
    private readonly EncounterPreviewRuntime _encounters;
    private readonly Queue<MissionSignal> _pending = new();
    private readonly HashSet<string> _activated = new();
    private readonly Dictionary<string, bool> _inside = new();
    private readonly Dictionary<string, string> _samples = new();
    internal readonly List<MissionActor> Actors = new();
    private double _started = UnityEngine.Time.realtimeSinceStartup;
    private double? _pausedAt;
    private float _nextSample;
    private readonly Dictionary<string, (WorldInteractiveObject Object, EDoorState State)> _interactions = new();

    internal void BindInteractions(IEnumerable<(string Id, WorldInteractiveObject Object)> interactions)
    {
        foreach (var (id, target) in interactions)
            if (target)
                _interactions[id] = (target, target.DoorState);
    }

    internal MissionLogicState State { get; private set; } = new();
    internal bool HasPending => _pending.Count > 0;
    internal double Time => _pausedAt ?? Math.Max(State.Time, UnityEngine.Time.realtimeSinceStartup - _started);

    internal void Pause() => _pausedAt ??= Time;

    internal void Resume()
    {
        if (_pausedAt.HasValue)
            _started = UnityEngine.Time.realtimeSinceStartup - _pausedAt.Value;
        _pausedAt = null;
    }

    internal void Restore(MissionLogicState state)
    {
        State = state;
        _started = UnityEngine.Time.realtimeSinceStartup - state.Time;
        _pausedAt = state.Time;
        _pending.Clear();
        _inside.Clear();
        _samples.Clear();
        _activated.Clear();
        Actors.Clear();
        foreach (var id in state.ActivatedEncounters)
            _activated.Add(id);
        foreach (var zone in state.Zones)
            _inside[zone.Key] = zone.Value.PlayerInside;
    }

    internal MissionDirector(MapLayout layout, Player player, EncounterPreviewRuntime encounters)
    {
        _layout = layout;
        _player = player;
        _encounters = encounters;
        encounters.Signal += Observe;
        encounters.ActorRegistered += Register;
    }

    private void Register(MissionActor actor) => Actors.Add(actor);

    internal void Observe(MissionSignal signal)
    {
        if (_pending.Count >= 4096)
            throw new InvalidOperationException("Mission observations are not being acknowledged.");
        signal.Time = Time;
        _pending.Enqueue(signal);
    }

    internal void Tick()
    {
        if (_pausedAt.HasValue)
            return;
        if (UnityEngine.Time.realtimeSinceStartup < _nextSample)
            return;
        _nextSample = UnityEngine.Time.realtimeSinceStartup + .5f;
        ObserveInteractions();
        SampleVolumes();
        if (
            Time - State.Time >= 1
            && (State.Timers.Count > 0 || State.Objectives.Values.AsValueEnumerable().Any(o => o.Status == "Active"))
        )
            Observe(new MissionSignal { Kind = MissionSignals.Tick });
    }

    internal void ObserveInteractions()
    {
        foreach (var pair in _interactions.AsValueEnumerable().ToArray())
        {
            var target = pair.Value.Object;
            if (!target || target.DoorState == pair.Value.State)
                continue;
            if (target.DoorState is EDoorState.Open or EDoorState.Shut)
                Observe(new MissionSignal { Kind = MissionSignals.Interaction, TargetId = pair.Key });
            _interactions[pair.Key] = (target, target.DoorState);
        }
    }

    private void SampleVolumes()
    {
        foreach (
            var volume in _layout
                .Checkpoints.AsValueEnumerable()
                .Concat(_layout.Exit == null ? Array.Empty<MapVolume>() : new[] { _layout.Exit })
        )
        {
            var inside = EncounterNavigation.Contains(volume, _player.Transform.position);
            if (!_inside.TryGetValue(volume.Id, out var previous))
                previous = false;
            if (inside != previous)
                Observe(new MissionSignal { Kind = inside ? MissionSignals.Enter : MissionSignals.Leave, TargetId = volume.Id });
            _inside[volume.Id] = inside;
            var occupants = _encounters.Occupants(volume);
            occupants.Sort(StringComparer.Ordinal);
            var sample = (inside ? "1:" : "0:") + string.Join(",", occupants);
            if (!_samples.TryGetValue(volume.Id, out var previousSample) || sample != previousSample)
            {
                Observe(
                    new MissionSignal
                    {
                        Kind = MissionSignals.Sample,
                        TargetId = volume.Id,
                        PlayerInside = inside,
                        Occupants = occupants,
                    }
                );
                _samples[volume.Id] = sample;
            }
        }
    }

    internal List<MissionSignal> Take()
    {
        var signals = new List<MissionSignal>();
        while (_pending.Count > 0 && signals.Count < 128)
            signals.Add(_pending.Dequeue());
        return signals;
    }

    internal void Accept(MissionLogicState state)
    {
        State = state;
        foreach (var id in state.ActivatedEncounters)
            if (_activated.Add(id))
                _encounters.ActivateEncounter(id);
    }

    public void Dispose()
    {
        _encounters.Signal -= Observe;
        _encounters.ActorRegistered -= Register;
        _pending.Clear();
    }
}
