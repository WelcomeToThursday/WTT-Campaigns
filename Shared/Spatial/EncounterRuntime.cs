namespace WTT.Campaigns.Shared.Spatial;

public static class EncounterRuntimeModes
{
    public const string Preview = "Preview";
    public const string Mission = "Mission";
}

/// <summary>Identity carried by every controlled encounter operation.</summary>
public sealed class EncounterRuntimeContext
{
    public string SessionId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public long LayoutRevision { get; set; }
    public string Mode { get; set; } = EncounterRuntimeModes.Preview;
    public string PreviewGeneration { get; set; } = "";
    public long AttemptGeneration { get; set; } = 1;
    public bool PublishedLayoutConfirmed { get; set; }

    public bool IsPreview => string.Equals(Mode, EncounterRuntimeModes.Preview, StringComparison.Ordinal);

    public bool HasIdentity =>
        !string.IsNullOrWhiteSpace(SessionId)
        && !string.IsNullOrWhiteSpace(RaidId)
        && !string.IsNullOrWhiteSpace(LayoutId)
        && !string.IsNullOrWhiteSpace(PreviewGeneration);

    public bool Matches(EncounterRuntimeContext other)
    {
        return other != null
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
            && string.Equals(RaidId, other.RaidId, StringComparison.Ordinal)
            && string.Equals(LayoutId, other.LayoutId, StringComparison.Ordinal)
            && LayoutRevision == other.LayoutRevision
            && string.Equals(Mode, other.Mode, StringComparison.Ordinal)
            && string.Equals(PreviewGeneration, other.PreviewGeneration, StringComparison.Ordinal)
            && AttemptGeneration == other.AttemptGeneration
            && PublishedLayoutConfirmed == other.PublishedLayoutConfirmed;
    }
}

/// <summary>A one-shot admission token for one generated profile and authored spawn point.</summary>
public sealed class EncounterSpawnReservation
{
    internal EncounterSpawnReservation(
        string token,
        EncounterRuntimeContext context,
        string encounterId,
        string profileId,
        string spawnPointId
    )
    {
        Token = token;
        SessionId = context.SessionId;
        RaidId = context.RaidId;
        LayoutId = context.LayoutId;
        LayoutRevision = context.LayoutRevision;
        Mode = context.Mode;
        PreviewGeneration = context.PreviewGeneration;
        AttemptGeneration = context.AttemptGeneration;
        PublishedLayoutConfirmed = context.PublishedLayoutConfirmed;
        EncounterId = encounterId;
        ProfileId = profileId;
        SpawnPointId = spawnPointId;
    }

    public string Token { get; }
    public string SessionId { get; }
    public string RaidId { get; }
    public string LayoutId { get; }
    public long LayoutRevision { get; }
    public string Mode { get; }
    public string PreviewGeneration { get; }
    public long AttemptGeneration { get; }
    public bool PublishedLayoutConfirmed { get; }
    public string EncounterId { get; }
    public string ProfileId { get; }
    public string SpawnPointId { get; }
}

/// <summary>
/// Admission is deliberately scoped to a generation, encounter and profile. It provides no global bot-spawn bypass.
/// </summary>
public sealed class EncounterSpawnAdmission
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EncounterSpawnReservation> _active = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumedTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidatedTokens = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedProfiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedSpawns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidatedContexts = new(StringComparer.Ordinal);

    public bool CanActivate(EncounterRuntimeContext? context, out string error)
    {
        error = "";
        if (context == null || !context.HasIdentity)
        {
            error = "Encounter runtime identity is incomplete.";
            return false;
        }

        if (context.IsPreview)
        {
            return true;
        }

        if (string.Equals(context.Mode, EncounterRuntimeModes.Mission, StringComparison.Ordinal) && context.PublishedLayoutConfirmed)
        {
            return true;
        }

        error = "Mission encounter spawning requires a server-confirmed published layout.";
        return false;
    }

    public bool TryReserve(
        EncounterRuntimeContext context,
        string encounterId,
        string profileId,
        string spawnPointId,
        out EncounterSpawnReservation? reservation,
        out string error
    )
    {
        reservation = null;
        if (!CanActivate(context, out error))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(encounterId) || string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(spawnPointId))
        {
            error = "Encounter reservations require encounter, profile and spawn identities.";
            return false;
        }

        var contextKey = ContextKey(context);
        // A generated profile is single-use for the whole preview generation. A spawn anchor is
        // reserved only until activation consumes its token; later finite waves may reuse it after
        // their own runtime occupancy/death tracking releases the bot.
        var profileKey = contextKey + "|profile|" + profileId;
        var spawnKey = contextKey + "|spawn|" + spawnPointId;
        lock (_gate)
        {
            if (_invalidatedContexts.Contains(contextKey))
            {
                error = "The encounter runtime generation has been invalidated.";
                return false;
            }

            if (_usedProfiles.Contains(profileKey))
            {
                error = "A generated profile can be admitted only once for an encounter generation.";
                return false;
            }

            if (_usedSpawns.Contains(spawnKey))
            {
                error = "An authored spawn point can be admitted only once for an encounter generation.";
                return false;
            }

            var token = Guid.NewGuid().ToString("N");
            reservation = new EncounterSpawnReservation(token, context, encounterId, profileId, spawnPointId);
            _active[token] = reservation;
            _usedProfiles.Add(profileKey);
            _usedSpawns.Add(spawnKey);
            error = "";
            return true;
        }
    }

    public bool TryReserve(
        EncounterRuntimeContext context,
        string encounterId,
        string profileId,
        string spawnPointId,
        out EncounterSpawnReservation? reservation
    )
    {
        return TryReserve(context, encounterId, profileId, spawnPointId, out reservation, out _);
    }

    public bool TryConsume(EncounterRuntimeContext context, EncounterSpawnReservation reservation, out string error)
    {
        error = "";
        if (reservation == null || !Matches(context, reservation))
        {
            error = "The spawn reservation does not belong to this runtime context.";
            return false;
        }

        lock (_gate)
        {
            if (_invalidatedTokens.Contains(reservation.Token))
            {
                error = "The spawn reservation was invalidated.";
                return false;
            }

            if (_consumedTokens.Contains(reservation.Token) || !_active.Remove(reservation.Token))
            {
                error = "Spawn reservations are single-use.";
                return false;
            }

            _consumedTokens.Add(reservation.Token);
            _usedSpawns.Remove(ContextKey(context) + "|spawn|" + reservation.SpawnPointId);
            return true;
        }
    }

    public bool TryConsume(EncounterRuntimeContext context, EncounterSpawnReservation reservation)
    {
        return TryConsume(context, reservation, out _);
    }

    /// <summary>Cancel one reservation before activation. Invalidating a context is stronger and rejects late results.</summary>
    public bool Cancel(EncounterRuntimeContext context, EncounterSpawnReservation reservation)
    {
        if (reservation == null || !Matches(context, reservation))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_active.Remove(reservation.Token))
            {
                return false;
            }

            _invalidatedTokens.Add(reservation.Token);
            _usedProfiles.Remove(ContextKey(context) + "|profile|" + reservation.ProfileId);
            _usedSpawns.Remove(ContextKey(context) + "|spawn|" + reservation.SpawnPointId);
            return true;
        }
    }

    /// <summary>Invalidates every outstanding token for this exact runtime generation.</summary>
    public int Invalidate(EncounterRuntimeContext context)
    {
        if (context == null)
        {
            return 0;
        }

        var key = ContextKey(context);
        lock (_gate)
        {
            _invalidatedContexts.Add(key);
            var tokens = _active.Values.AsValueEnumerable().Where(r => Matches(context, r)).Select(r => r.Token).ToArray();
            foreach (var token in tokens)
            {
                _active.Remove(token);
                _invalidatedTokens.Add(token);
            }

            return tokens.Length;
        }
    }

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _active.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _active.Clear();
            _consumedTokens.Clear();
            _invalidatedTokens.Clear();
            _usedProfiles.Clear();
            _usedSpawns.Clear();
            _invalidatedContexts.Clear();
        }
    }

    private static bool Matches(EncounterRuntimeContext context, EncounterSpawnReservation reservation)
    {
        return context != null
            && context.SessionId == reservation.SessionId
            && context.RaidId == reservation.RaidId
            && context.LayoutId == reservation.LayoutId
            && context.LayoutRevision == reservation.LayoutRevision
            && context.Mode == reservation.Mode
            && context.PreviewGeneration == reservation.PreviewGeneration
            && context.AttemptGeneration == reservation.AttemptGeneration
            && context.PublishedLayoutConfirmed == reservation.PublishedLayoutConfirmed;
    }

    private static string ContextKey(EncounterRuntimeContext context)
    {
        return string.Join(
            "|",
            context.SessionId,
            context.RaidId,
            context.LayoutId,
            context.LayoutRevision,
            context.Mode,
            context.PreviewGeneration,
            context.AttemptGeneration,
            context.PublishedLayoutConfirmed ? "1" : "0"
        );
    }
}

public enum EncounterWaveStatus
{
    Pending,
    Ready,
    Generating,
    Active,
    Completed,
    Failed,
    Cancelled,
}

public sealed class EncounterWaveRuntimeState
{
    internal EncounterWaveRuntimeState(int index, MapEncounterWave wave)
    {
        Index = index;
        WaveId = wave.Id;
        ExpectedBots = wave.Roster?.AsValueEnumerable().Where(r => r != null).Sum(r => Math.Max(r.Count, 0)) ?? 0;
        Status = EncounterWaveStatus.Pending;
    }

    public int Index { get; }
    public string WaveId { get; }
    public int ExpectedBots { get; }
    public EncounterWaveStatus Status { get; internal set; }
    public string GenerationId { get; internal set; } = "";
    public double? ActivatedAt { get; internal set; }
    public double? CompletedAt { get; internal set; }
    public string FailureReason { get; internal set; } = "";
    public int ReservedBots { get; internal set; }
    public int ActiveBots { get; internal set; }
    public IReadOnlyCollection<string> ActiveProfileIds => _activeProfileIds;

    private readonly HashSet<string> _activeProfileIds = new(StringComparer.Ordinal);

    internal void AddActive(string profileId)
    {
        _activeProfileIds.Add(profileId);
        ActiveBots = _activeProfileIds.Count;
    }

    internal bool RemoveActive(string profileId)
    {
        var removed = _activeProfileIds.Remove(profileId);
        ActiveBots = _activeProfileIds.Count;
        return removed;
    }

    internal void Clear()
    {
        _activeProfileIds.Clear();
        ActiveBots = 0;
        ReservedBots = 0;
        GenerationId = "";
        ActivatedAt = null;
        CompletedAt = null;
        FailureReason = "";
        Status = EncounterWaveStatus.Pending;
    }
}

public sealed class EncounterWaveReservation
{
    public EncounterWaveReservation(string rosterId, string profileId, string spawnPointId)
    {
        RosterId = rosterId;
        ProfileId = profileId;
        SpawnPointId = spawnPointId;
    }

    public string RosterId { get; }
    public string ProfileId { get; }
    public string SpawnPointId { get; }
}

/// <summary>Deterministic encounter trigger, generation and death-gated wave state.</summary>
public sealed class EncounterWaveStateMachine
{
    private readonly MapEncounter _encounter;
    private readonly List<EncounterWaveRuntimeState> _states;
    private readonly Dictionary<int, List<EncounterWaveReservation>> _reservations = new();
    private bool _activated;
    private bool _halted;
    private string _activationKey = "";
    private double _activationTime;

    public EncounterWaveStateMachine(MapEncounter encounter)
    {
        _encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
        _states = (encounter.Waves ?? new())
            .AsValueEnumerable()
            .Select((wave, index) => new EncounterWaveRuntimeState(index, wave))
            .ToList();
    }

    public MapEncounter Encounter => _encounter;
    public bool IsActivated => _activated;
    public bool IsFailed => _halted;
    public IReadOnlyList<EncounterWaveRuntimeState> Waves => _states;

    /// <summary>Capture only settled waves; callers hold scheduling and await native generation first.</summary>
    public EncounterCheckpoint Capture(double now)
    {
        if (!double.IsFinite(now) || _halted || _states.AsValueEnumerable().Any(s => s.Status == EncounterWaveStatus.Generating))
            throw new InvalidOperationException("Encounter generation must settle before capturing a checkpoint.");
        return new EncounterCheckpoint
        {
            EncounterId = _encounter.Id,
            Activated = _activated,
            ActivationKey = _activationKey,
            ActivationOffset = _activationTime - now,
            Waves = _states
                .AsValueEnumerable()
                .Select(s => new EncounterWaveCheckpoint
                {
                    WaveId = s.WaveId,
                    Status = s.Status == EncounterWaveStatus.Ready ? EncounterWaveStatus.Pending : s.Status,
                    ActivatedOffset = s.ActivatedAt - now,
                    CompletedOffset = s.CompletedAt - now,
                    LivingProfileIds = s.ActiveProfileIds.AsValueEnumerable().ToList(),
                })
                .ToList(),
        };
    }

    /// <summary>Rebase mission delays without changing the native raid deadline. Old generation callbacks cannot commit.</summary>
    public void Restore(EncounterCheckpoint checkpoint, double now, IReadOnlyDictionary<string, string> replacementIds)
    {
        if (
            !double.IsFinite(now)
            || checkpoint.EncounterId != _encounter.Id
            || checkpoint.Waves.Count != _states.Count
            || !double.IsFinite(checkpoint.ActivationOffset)
        )
            throw new InvalidOperationException("The encounter checkpoint does not match this encounter.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _states.Count; i++)
        {
            var saved = checkpoint.Waves[i];
            if (
                saved.WaveId != _states[i].WaveId
                || saved.Status is not (EncounterWaveStatus.Pending or EncounterWaveStatus.Active or EncounterWaveStatus.Completed)
                || (saved.ActivatedOffset.HasValue && !double.IsFinite(saved.ActivatedOffset.Value))
                || (saved.CompletedOffset.HasValue && !double.IsFinite(saved.CompletedOffset.Value))
                || saved.LivingProfileIds.Count > _states[i].ExpectedBots
                || (saved.Status != EncounterWaveStatus.Active && saved.LivingProfileIds.Count != 0)
                || (saved.Status == EncounterWaveStatus.Active && (saved.LivingProfileIds.Count == 0 || !saved.ActivatedOffset.HasValue))
                || (saved.Status == EncounterWaveStatus.Completed && !saved.CompletedOffset.HasValue)
            )
                throw new InvalidOperationException("The encounter wave checkpoint is invalid.");
            foreach (var oldId in saved.LivingProfileIds)
                if (
                    !replacementIds.TryGetValue(oldId, out var newId)
                    || string.IsNullOrWhiteSpace(newId)
                    || newId == oldId
                    || !identities.Add(newId)
                )
                    throw new InvalidOperationException("A checkpoint bot requires a fresh, unique runtime identity.");
        }
        Reset();
        _activated = checkpoint.Activated;
        _activationKey = checkpoint.ActivationKey;
        _activationTime = now + checkpoint.ActivationOffset;
        for (var i = 0; i < _states.Count; i++)
        {
            var saved = checkpoint.Waves[i];
            var state = _states[i];
            state.Status = saved.Status;
            state.ActivatedAt = saved.ActivatedOffset + now;
            state.CompletedAt = saved.CompletedOffset + now;
            foreach (var oldId in saved.LivingProfileIds)
                state.AddActive(replacementIds[oldId]);
        }
    }

    public bool TryActivate(string activationKey, double now, out string error)
    {
        error = "";
        if (_activated)
        {
            error = "Encounter activation is already consumed for this run.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(activationKey) || !double.IsFinite(now))
        {
            error = "Encounter activation requires a key and finite time.";
            return false;
        }

        if (_states.Count == 0)
        {
            error = "An encounter requires at least one wave.";
            _halted = true;
            return false;
        }

        _activated = true;
        _activationKey = activationKey;
        _activationTime = now;
        return true;
    }

    public bool TryActivate(string activationKey, double now)
    {
        return TryActivate(activationKey, now, out _);
    }

    /// <summary>Returns each wave that is newly eligible and marks it Ready exactly once.</summary>
    public IReadOnlyList<int> ReadyWaves(double now)
    {
        if (!_activated || _halted || !double.IsFinite(now))
        {
            return Array.Empty<int>();
        }

        var ready = new List<int>();
        for (var index = 0; index < _states.Count; index++)
        {
            var state = _states[index];
            if (state.Status != EncounterWaveStatus.Pending)
            {
                continue;
            }

            var wave = _encounter.Waves![index];
            var due = index == 0 ? now >= _activationTime + wave.DelaySeconds : DueAfterPrevious(index, wave, now);
            if (due)
            {
                state.Status = EncounterWaveStatus.Ready;
                ready.Add(index);
            }
        }

        return ready;
    }

    public bool TryBeginGeneration(int waveIndex, string generationId, out string error)
    {
        error = "";
        if (!TryGetState(waveIndex, out var state))
        {
            error = "Unknown encounter wave.";
            return false;
        }

        if (state.Status != EncounterWaveStatus.Ready)
        {
            error = "The encounter wave is not ready for generation.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(generationId) || _states.AsValueEnumerable().Any(s => s.GenerationId == generationId))
        {
            error = "Each wave generation requires a unique identity.";
            return false;
        }

        state.Status = EncounterWaveStatus.Generating;
        state.GenerationId = generationId;
        state.ReservedBots = 0;
        _reservations[waveIndex] = new();
        return true;
    }

    public bool TryBeginGeneration(int waveIndex, string generationId)
    {
        return TryBeginGeneration(waveIndex, generationId, out _);
    }

    public bool TryReserveBot(int waveIndex, string generationId, string rosterId, string profileId, string spawnPointId, out string error)
    {
        error = "";
        if (!TryGetState(waveIndex, out var state) || state.Status != EncounterWaveStatus.Generating)
        {
            error = "The encounter wave is not generating.";
            return false;
        }

        if (!string.Equals(state.GenerationId, generationId, StringComparison.Ordinal))
        {
            error = "The generation identity is stale.";
            return false;
        }

        var wave = _encounter.Waves![waveIndex];
        var roster = wave.Roster?.AsValueEnumerable().FirstOrDefault(r => r != null && r.Id == rosterId);
        if (roster == null)
        {
            error = "The profile roster entry is unknown.";
            return false;
        }

        var reservations = _reservations[waveIndex];
        if (reservations.AsValueEnumerable().Any(r => r.ProfileId == profileId || r.SpawnPointId == spawnPointId))
        {
            error = "Profiles and authored spawn points can be reserved only once per wave.";
            return false;
        }

        if (reservations.AsValueEnumerable().Count(r => r.RosterId == rosterId) >= roster.Count)
        {
            error = "The roster count has already been reserved.";
            return false;
        }

        if (roster.SpawnPointIds == null || !roster.SpawnPointIds.AsValueEnumerable().Contains(spawnPointId, StringComparer.Ordinal))
        {
            error = "The spawn point is not authored for this roster entry.";
            return false;
        }

        reservations.Add(new EncounterWaveReservation(rosterId, profileId, spawnPointId));
        state.ReservedBots = reservations.Count;
        return true;
    }

    public bool TryReserveBot(int waveIndex, string generationId, EncounterWaveReservation reservation, out string error)
    {
        if (reservation == null)
        {
            error = "A wave reservation is required.";
            return false;
        }

        return TryReserveBot(waveIndex, generationId, reservation.RosterId, reservation.ProfileId, reservation.SpawnPointId, out error);
    }

    public bool TryCommitGeneration(int waveIndex, string generationId, double now, out string error)
    {
        error = "";
        if (!TryGetState(waveIndex, out var state) || state.Status != EncounterWaveStatus.Generating)
        {
            error = "The encounter wave is not generating.";
            return false;
        }

        if (state.GenerationId != generationId)
        {
            error = "The generation identity is stale.";
            return false;
        }

        var reservations = _reservations.GetValueOrDefault(waveIndex) ?? new();
        var wave = _encounter.Waves![waveIndex];
        var counts = reservations
            .AsValueEnumerable()
            .GroupBy(r => r.RosterId)
            .ToDictionary(g => g.Key, g => g.AsValueEnumerable().Count(), StringComparer.Ordinal);
        var complete =
            double.IsFinite(now)
            && reservations.Count == state.ExpectedBots
            && wave.Roster != null
            && wave.Roster.AsValueEnumerable().All(r => r != null && counts.GetValueOrDefault(r.Id) == r.Count);
        if (!complete)
        {
            FailGeneration(waveIndex, generationId, "Profile generation or authored spawn reservations were incomplete.");
            error = state.FailureReason;
            return false;
        }

        state.Status = EncounterWaveStatus.Active;
        state.ActivatedAt = now;
        state.CompletedAt = null;
        foreach (var reservation in reservations)
        {
            state.AddActive(reservation.ProfileId);
        }

        return true;
    }

    public bool TryCommitGeneration(int waveIndex, string generationId, double now)
    {
        return TryCommitGeneration(waveIndex, generationId, now, out _);
    }

    public bool FailGeneration(int waveIndex, string generationId, string reason)
    {
        if (!TryGetState(waveIndex, out var state) || state.Status != EncounterWaveStatus.Generating || state.GenerationId != generationId)
        {
            return false;
        }

        state.Status = EncounterWaveStatus.Failed;
        state.FailureReason = string.IsNullOrWhiteSpace(reason) ? "Encounter wave generation failed." : reason;
        _halted = true;
        return true;
    }

    public bool MarkBotDefeated(int waveIndex, string profileId, double now)
    {
        if (!TryGetState(waveIndex, out var state) || state.Status != EncounterWaveStatus.Active || !double.IsFinite(now))
        {
            return false;
        }

        if (!state.RemoveActive(profileId))
        {
            return false;
        }

        if (state.ActiveBots == 0)
        {
            state.Status = EncounterWaveStatus.Completed;
            state.CompletedAt = now;
        }

        return true;
    }

    /// <summary>A missing or failed bot is never treated as a death-gated completion.</summary>
    public bool MarkBotFailed(int waveIndex, string profileId, string reason)
    {
        if (
            !TryGetState(waveIndex, out var state)
            || state.Status != EncounterWaveStatus.Active
            || !state.ActiveProfileIds.AsValueEnumerable().Contains(profileId)
        )
        {
            return false;
        }

        state.Status = EncounterWaveStatus.Failed;
        state.FailureReason = string.IsNullOrWhiteSpace(reason) ? "An encounter bot failed outside the death path." : reason;
        _halted = true;
        return true;
    }

    public void Cancel()
    {
        _halted = true;
        foreach (
            var state in _states
                .AsValueEnumerable()
                .Where(s =>
                    s.Status
                        is EncounterWaveStatus.Pending
                            or EncounterWaveStatus.Ready
                            or EncounterWaveStatus.Generating
                            or EncounterWaveStatus.Active
                )
        )
        {
            state.Status = EncounterWaveStatus.Cancelled;
        }
    }

    public void Reset()
    {
        _activated = false;
        _halted = false;
        _activationKey = "";
        _activationTime = 0;
        _reservations.Clear();
        foreach (var state in _states)
        {
            state.Clear();
        }
    }

    private bool DueAfterPrevious(int index, MapEncounterWave wave, double now)
    {
        var previous = _states[index - 1];
        if (previous.Status is EncounterWaveStatus.Failed or EncounterWaveStatus.Cancelled || previous.ActivatedAt == null)
        {
            return false;
        }

        return wave.WaitForPreviousWave
            ? previous.Status == EncounterWaveStatus.Completed
                && previous.CompletedAt != null
                && now >= previous.CompletedAt.Value + wave.DelaySeconds
            : now >= previous.ActivatedAt.Value + wave.DelaySeconds;
    }

    private bool TryGetState(int index, out EncounterWaveRuntimeState state)
    {
        if (index >= 0 && index < _states.Count)
        {
            state = _states[index];
            return true;
        }

        state = null!;
        return false;
    }
}

public enum PatrolBotControlState
{
    Unknown,
    Eligible,
    Combat,
    Searching,
    Recovery,
}

public sealed class PatrolBotSnapshot
{
    public string BotId { get; set; } = "";
    public bool Alive { get; set; } = true;
    public PatrolBotControlState ControlState { get; set; }
    public SpatialVector Position { get; set; } = new();
}

public interface IPatrolNavigation
{
    bool CanReach(SpatialVector from, SpatialVector to);
}

public interface IPatrolNavigationBudget : IPatrolNavigation
{
    bool Deferred { get; }
}

public enum PatrolRuntimeStatus
{
    Inactive,
    Moving,
    Waiting,
    Suspended,
    Completed,
    Failed,
    Cancelled,
}

public enum PatrolSuspensionReason
{
    None,
    UnknownState,
    Combat,
    Searching,
    Recovery,
    Unreachable,
    NoSurvivors,
}

public sealed class PatrolMovementCommand
{
    public string BotId { get; internal set; } = "";
    public int WaypointIndex { get; internal set; }
    public SpatialVector Target { get; internal set; } = new();
    public string Pace { get; internal set; } = MapPatrolRoute.Walk;
}

public sealed class PatrolUpdate
{
    public PatrolRuntimeStatus Status { get; internal set; }
    public PatrolSuspensionReason SuspensionReason { get; internal set; }
    public string LeaderId { get; internal set; } = "";
    public bool LeaderChanged { get; internal set; }
    public bool Rejoined { get; internal set; }
    public int TargetWaypointIndex { get; internal set; } = -1;
    public IReadOnlyList<PatrolMovementCommand> Commands { get; internal set; } = Array.Empty<PatrolMovementCommand>();
}

/// <summary>Pure squad patrol ownership state. It emits movement only while every survivor is SAIN-eligible.</summary>
public sealed class EncounterPatrolStateMachine
{
    private readonly MapPatrolRoute _route;
    private readonly List<string> _orderedBotIds = new();
    private int _targetWaypoint = -1;
    private int _direction = 1;
    private double? _waitUntil;
    private double? _suspendedWait;
    private string _leaderId = "";
    private PatrolRuntimeStatus _status = PatrolRuntimeStatus.Inactive;
    private PatrolSuspensionReason _reason;

    public EncounterPatrolStateMachine(MapPatrolRoute route)
    {
        _route = route ?? throw new ArgumentNullException(nameof(route));
    }

    public MapPatrolRoute Route => _route;

    public void SuspendNavigation(double now)
    {
        if (_status is PatrolRuntimeStatus.Completed or PatrolRuntimeStatus.Cancelled or PatrolRuntimeStatus.Failed)
            return;
        if (_waitUntil.HasValue)
        {
            _suspendedWait = Math.Max(0, _waitUntil.Value - now);
            _waitUntil = null;
        }
        _status = PatrolRuntimeStatus.Suspended;
        _reason = PatrolSuspensionReason.Unreachable;
    }

    public PatrolRuntimeStatus Status => _status;
    public PatrolSuspensionReason SuspensionReason => _reason;
    public string LeaderId => _leaderId;
    public int TargetWaypointIndex => _targetWaypoint;

    public PatrolCheckpoint Capture(double now) =>
        new()
        {
            RouteId = _route.Id,
            Waypoint = _targetWaypoint,
            Direction = _direction,
            WaitRemaining = _suspendedWait ?? (_waitUntil.HasValue ? Math.Max(0, _waitUntil.Value - now) : null),
            Completed = _status == PatrolRuntimeStatus.Completed,
        };

    public void Restore(PatrolCheckpoint checkpoint, double now)
    {
        if (
            checkpoint.RouteId != _route.Id
            || checkpoint.Waypoint < -1
            || checkpoint.Waypoint >= _route.Waypoints.Count
            || checkpoint.Direction is not (1 or -1)
            || !double.IsFinite(now)
            || (checkpoint.WaitRemaining.HasValue && (!double.IsFinite(checkpoint.WaitRemaining.Value) || checkpoint.WaitRemaining < 0))
        )
            throw new InvalidOperationException("The checkpoint patrol assignment is invalid.");
        _targetWaypoint = checkpoint.Waypoint;
        _suspendedWait = null;
        _direction = checkpoint.Direction;
        _waitUntil = checkpoint.WaitRemaining.HasValue ? now + checkpoint.WaitRemaining.Value : null;
        _status =
            checkpoint.Completed ? PatrolRuntimeStatus.Completed
            : _waitUntil.HasValue ? PatrolRuntimeStatus.Waiting
            : _targetWaypoint < 0 ? PatrolRuntimeStatus.Inactive
            : PatrolRuntimeStatus.Moving;
        _reason = PatrolSuspensionReason.None;
        _leaderId = "";
    }

    public bool Start(IEnumerable<string> orderedBotIds, out string error)
    {
        error = "";
        _orderedBotIds.Clear();
        _orderedBotIds.AddRange(
            (orderedBotIds ?? Array.Empty<string>()).AsValueEnumerable().Where(id => !string.IsNullOrWhiteSpace(id)).ToArray()
        );
        if (_orderedBotIds.Count == 0)
        {
            error = "A patrol requires at least one bot.";
            _status = PatrolRuntimeStatus.Failed;
            _reason = PatrolSuspensionReason.NoSurvivors;
            return false;
        }

        if (_route.Waypoints == null || _route.Waypoints.Count < 2)
        {
            error = "A patrol requires at least two waypoints.";
            _status = PatrolRuntimeStatus.Failed;
            return false;
        }

        if (_orderedBotIds.AsValueEnumerable().Distinct(StringComparer.Ordinal).Count() != _orderedBotIds.Count)
        {
            error = "Patrol bot identities must be unique.";
            _status = PatrolRuntimeStatus.Failed;
            return false;
        }

        _targetWaypoint = -1;
        _direction = 1;
        _waitUntil = null;
        _suspendedWait = null;
        _leaderId = "";
        _status = PatrolRuntimeStatus.Inactive;
        _reason = PatrolSuspensionReason.None;
        return true;
    }

    public bool Start(IEnumerable<string> orderedBotIds)
    {
        return Start(orderedBotIds, out _);
    }

    /// <summary>Update the roster without restarting an existing patrol.</summary>
    public void UpdateMembers(IEnumerable<string> orderedBotIds)
    {
        var ids = orderedBotIds.AsValueEnumerable().ToArray();
        if (
            ids.Length == 0
            || ids.AsValueEnumerable().Any(string.IsNullOrWhiteSpace)
            || ids.AsValueEnumerable().Distinct(StringComparer.Ordinal).Count() != ids.Length
        )
            throw new ArgumentException("Patrol bot identities must be nonempty and unique.", nameof(orderedBotIds));
        _orderedBotIds.Clear();
        _orderedBotIds.AddRange(ids);
    }

    public bool TryUpdateBudgeted(
        IEnumerable<PatrolBotSnapshot> snapshots,
        double now,
        IPatrolNavigation navigation,
        out PatrolUpdate update
    )
    {
        var saved = (_targetWaypoint, _direction, _waitUntil, _suspendedWait, _leaderId, _status, _reason);
        update = Update(snapshots, now, navigation);
        if (navigation is IPatrolNavigationBudget { Deferred: true })
        {
            (_targetWaypoint, _direction, _waitUntil, _suspendedWait, _leaderId, _status, _reason) = saved;
            update = new PatrolUpdate
            {
                Status = _status,
                SuspensionReason = _reason,
                LeaderId = _leaderId,
                TargetWaypointIndex = _targetWaypoint,
            };
            return false;
        }
        return true;
    }

    public PatrolUpdate Update(IEnumerable<PatrolBotSnapshot> snapshots, double now, IPatrolNavigation navigation)
    {
        var result = new PatrolUpdate
        {
            Status = _status,
            SuspensionReason = _reason,
            TargetWaypointIndex = _targetWaypoint,
        };
        if (
            !double.IsFinite(now)
            || navigation == null
            || _status is PatrolRuntimeStatus.Failed or PatrolRuntimeStatus.Completed or PatrolRuntimeStatus.Cancelled
        )
        {
            return result;
        }

        var byId = (snapshots ?? Array.Empty<PatrolBotSnapshot>())
            .AsValueEnumerable()
            .Where(s => s != null && _orderedBotIds.AsValueEnumerable().Contains(s.BotId, StringComparer.Ordinal))
            .GroupBy(s => s.BotId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.AsValueEnumerable().Last(), StringComparer.Ordinal);
        var survivors = _orderedBotIds
            .AsValueEnumerable()
            .Where(id => byId.TryGetValue(id, out var snapshot) && snapshot.Alive)
            .Select(id => byId[id])
            .ToArray();
        if (survivors.Length == 0)
        {
            _status = PatrolRuntimeStatus.Failed;
            _reason = PatrolSuspensionReason.NoSurvivors;
            return Snapshot(result);
        }

        var leader =
            _orderedBotIds.AsValueEnumerable().FirstOrDefault(id => byId.TryGetValue(id, out var snapshot) && snapshot.Alive) ?? "";
        var leaderChanged = leader != _leaderId;
        _leaderId = leader;

        var missing = _orderedBotIds.AsValueEnumerable().Any(id => !byId.ContainsKey(id));
        var blocker =
            missing ? PatrolSuspensionReason.UnknownState
            : survivors.AsValueEnumerable().Any(s => s.ControlState == PatrolBotControlState.Combat) ? PatrolSuspensionReason.Combat
            : survivors.AsValueEnumerable().Any(s => s.ControlState == PatrolBotControlState.Searching) ? PatrolSuspensionReason.Searching
            : survivors.AsValueEnumerable().Any(s => s.ControlState == PatrolBotControlState.Recovery) ? PatrolSuspensionReason.Recovery
            : survivors.AsValueEnumerable().Any(s => s.ControlState != PatrolBotControlState.Eligible) ? PatrolSuspensionReason.UnknownState
            : PatrolSuspensionReason.None;
        if (blocker != PatrolSuspensionReason.None)
        {
            if (_waitUntil.HasValue)
            {
                _suspendedWait = Math.Max(0, _waitUntil.Value - now);
                _waitUntil = null;
            }
            _status = PatrolRuntimeStatus.Suspended;
            _reason = blocker;
            return Snapshot(result, leaderChanged);
        }

        var leaderSnapshot = byId[_leaderId];
        if (_status == PatrolRuntimeStatus.Suspended)
        {
            _waitUntil = _suspendedWait.HasValue ? now + _suspendedWait.Value : null;
            _suspendedWait = null;
            _status = _waitUntil.HasValue ? PatrolRuntimeStatus.Waiting : PatrolRuntimeStatus.Moving;
            _reason = PatrolSuspensionReason.None;
        }
        if (_status == PatrolRuntimeStatus.Inactive || _targetWaypoint < 0)
        {
            if (!TryNearestReachable(leaderSnapshot.Position, survivors, navigation, out var initial))
            {
                _status = PatrolRuntimeStatus.Suspended;
                _reason = PatrolSuspensionReason.Unreachable;
                return Snapshot(result, leaderChanged);
            }

            _targetWaypoint = initial;
            _status = PatrolRuntimeStatus.Moving;
            _reason = PatrolSuspensionReason.None;
        }

        if (_status == PatrolRuntimeStatus.Waiting)
        {
            if (_waitUntil != null && now < _waitUntil.Value)
            {
                return Snapshot(result, leaderChanged);
            }

            _status = PatrolRuntimeStatus.Moving;
            _waitUntil = null;
        }

        if (_status != PatrolRuntimeStatus.Moving || _targetWaypoint < 0 || _targetWaypoint >= _route.Waypoints.Count)
        {
            return Snapshot(result, leaderChanged);
        }

        var target = _route.Waypoints[_targetWaypoint];
        if (target == null || survivors.AsValueEnumerable().Any(s => !navigation.CanReach(s.Position, target.Position)))
        {
            if (_route.Spline != null)
            {
                SuspendNavigation(now);
                return Snapshot(result, leaderChanged);
            }
            if (!TryNearestReachable(leaderSnapshot.Position, survivors, navigation, out var reentry))
            {
                _status = PatrolRuntimeStatus.Suspended;
                _reason = PatrolSuspensionReason.Unreachable;
                return Snapshot(result, leaderChanged);
            }
            _targetWaypoint = reentry;
            target = _route.Waypoints[reentry];
            result.Rejoined = true;
        }

        _reason = PatrolSuspensionReason.None;
        result.Commands = survivors
            .AsValueEnumerable()
            .Select(s => new PatrolMovementCommand
            {
                BotId = s.BotId,
                WaypointIndex = _targetWaypoint,
                Target = target.Position,
                Pace = _route.Pace,
            })
            .ToArray();
        return Snapshot(result, leaderChanged);
    }

    /// <summary>Advance only after the elected leader reaches the current target.</summary>
    public bool AcknowledgeWaypoint(string botId, int waypointIndex, double now)
    {
        if (_status != PatrolRuntimeStatus.Moving || botId != _leaderId || waypointIndex != _targetWaypoint || !double.IsFinite(now))
        {
            return false;
        }

        var wait = _route.WaitSeconds is { Count: > 0 } waits && waypointIndex < waits.Count ? waits[waypointIndex] : 0;
        var next = waypointIndex + _direction;
        switch (_route.Completion)
        {
            case MapPatrolRoute.Loop:
                next = (waypointIndex + 1) % _route.Waypoints.Count;
                _direction = 1;
                break;
            case MapPatrolRoute.PingPong:
                if (next >= _route.Waypoints.Count || next < 0)
                {
                    _direction *= -1;
                    next = waypointIndex + _direction;
                }
                break;
            case MapPatrolRoute.Stop:
                if (waypointIndex == _route.Waypoints.Count - 1)
                {
                    _status = PatrolRuntimeStatus.Completed;
                    _waitUntil = null;
                    return true;
                }
                _direction = 1;
                next = waypointIndex + 1;
                break;
            default:
                _status = PatrolRuntimeStatus.Failed;
                return false;
        }

        _targetWaypoint = next;
        if (float.IsFinite(wait) && wait > 0)
        {
            _status = PatrolRuntimeStatus.Waiting;
            _waitUntil = now + wait;
        }
        else
        {
            _status = PatrolRuntimeStatus.Moving;
            _waitUntil = null;
        }

        return true;
    }

    public void Cancel()
    {
        _status = PatrolRuntimeStatus.Cancelled;
        _reason = PatrolSuspensionReason.None;
    }

    public void Reset()
    {
        _targetWaypoint = -1;
        _direction = 1;
        _waitUntil = null;
        _suspendedWait = null;
        _leaderId = "";
        _status = PatrolRuntimeStatus.Inactive;
        _reason = PatrolSuspensionReason.None;
    }

    private bool TryNearestReachable(SpatialVector position, PatrolBotSnapshot[] survivors, IPatrolNavigation navigation, out int waypoint)
    {
        waypoint = -1;
        if (position?.Finite != true || _route.Waypoints == null)
        {
            return false;
        }

        var best = float.PositiveInfinity;
        for (var i = 0; i < _route.Waypoints.Count; i++)
        {
            var candidate = _route.Waypoints[i];
            if (
                candidate?.Position?.Finite != true
                || survivors.AsValueEnumerable().Any(s => !navigation.CanReach(s.Position, candidate.Position))
            )
            {
                continue;
            }

            var dx = position.X - candidate.Position.X;
            var dy = position.Y - candidate.Position.Y;
            var dz = position.Z - candidate.Position.Z;
            var distance = dx * dx + dy * dy + dz * dz;
            if (distance < best)
            {
                best = distance;
                waypoint = i;
            }
        }

        return waypoint >= 0;
    }

    private PatrolUpdate Snapshot(PatrolUpdate result, bool leaderChanged = false)
    {
        result.Status = _status;
        result.SuspensionReason = _reason;
        result.LeaderId = _leaderId;
        result.LeaderChanged = leaderChanged;
        result.TargetWaypointIndex = _targetWaypoint;
        return result;
    }
}
