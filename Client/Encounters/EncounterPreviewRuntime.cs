using Cysharp.Threading.Tasks;
using EFT;
using EFT.HealthSystem;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Encounters;

/// <summary>One disposable run; all callbacks retain this generation rather than a global current run.</summary>
internal sealed class EncounterPreviewRuntime
{
    private sealed class BotRecord
    {
        internal BotOwner Bot = null!;
        internal Player Player = null!;
        internal ActiveHealthController Health = null!;
        internal string ProfileId = "";
        internal EncounterWaveStateMachine Encounter = null!;
        internal int Wave;
        internal bool Finished;
        internal bool DeathConfirmed;
        internal string RosterId = "",
            Squad = "",
            Route = "";
        internal SpatialCapture Spawn = null!;

        internal void OnDeath(EDamageType _) => DeathConfirmed = true;
    }

    private readonly EncounterNative _native = new();
    private readonly EncounterNavigation _navigation = new();
    private readonly List<EncounterWaveStateMachine> _encounters = new();
    private readonly List<BotRecord> _bots = new();
    private readonly Dictionary<string, SpatialCapture> _anchors = new(StringComparer.Ordinal);
    private readonly List<SpatialCapture> _pendingAnchors = new();
    private readonly List<Task> _work = new();
    private EncounterRuntimeContext _context = null!;
    private MapLayout _layout = null!;
    private Player _player = null!;
    private string _encounterToken = "";
    private IEncounterAiRuntime? _ai;
    private CancellationTokenSource? _lifetime;
    private bool _ready,
        _ended;
    private float _nextUpdate;
    private float _nextBotTrace;
    private bool _paused;
    private float _clockOffset,
        _pausedAt;
    private float Clock => _paused ? _pausedAt : Time.time - _clockOffset;
    private Dictionary<string, PatrolCheckpoint>? _pausedPatrols;
    internal MissionRetryGuard? RetryGuard { get; set; }

    internal sealed class SavedBot
    {
        internal string ProfileId = "",
            ProfileJson = "",
            EncounterId = "",
            RosterId = "",
            Squad = "",
            Route = "";
        internal int Wave;
        internal SpatialCapture Spawn = null!;
        internal BotOwner OriginalBot = null!;
        internal MissionActorSnapshot Health = null!;
        internal bool PlayerWasEnemy;
        internal WildSpawnType Brain;
    }

    internal sealed class Checkpoint
    {
        internal List<SavedBot> Bots = new();
        internal List<EncounterCheckpoint> Encounters = new();
        internal Dictionary<string, PatrolCheckpoint> Patrols = new();
        internal HashSet<string> BaselineItems = new();
    }

    internal async Task SettleAsync(CancellationToken token)
    {
        if (!_paused)
        {
            _pausedAt = Clock;
            _pausedPatrols = _ai?.CapturePatrols();
        }
        _paused = true;
        await Task.WhenAll(_work.AsValueEnumerable().ToArray());
        token.ThrowIfCancellationRequested();
        Tick(true);
        if (Failure != null)
            throw new InvalidOperationException(Failure);
    }

    internal void Resume()
    {
        if (!_paused)
            return;
        _clockOffset = Time.time - _pausedAt;
        if (_pausedPatrols != null)
            _ai!.RestorePatrols(_pausedPatrols);
        _pausedPatrols = null;
        _paused = false;
    }

    internal Checkpoint Capture()
    {
        if (!_paused || _work.AsValueEnumerable().Any(t => !t.IsCompleted))
            throw new InvalidOperationException("Encounter work must settle before checkpoint capture.");
        var result = new Checkpoint { Patrols = _pausedPatrols ?? _ai!.CapturePatrols(), BaselineItems = _native.CaptureObjectBaseline() };
        foreach (var encounter in _encounters)
            result.Encounters.Add(encounter.Capture(Clock));
        foreach (var record in _bots)
        {
            if (record.DeathConfirmed || record.Finished)
                continue;
            result.Bots.Add(
                new SavedBot
                {
                    ProfileId = record.ProfileId,
                    EncounterId = record.Encounter.Encounter.Id,
                    RosterId = record.RosterId,
                    Wave = record.Wave,
                    Squad = record.Squad,
                    Route = record.Route,
                    Spawn = record.Spawn,
                    OriginalBot = record.Bot,
                    Health = new MissionActorSnapshot(record.Player),
                    PlayerWasEnemy = record.Bot.BotsGroup.IsEnemy(_player),
                    Brain = EncounterBrainChoice.Capture(record.Bot),
                    ProfileJson = JsonConvert.SerializeObject(
                        new ProfileDescriptor(record.Player.Profile, record.Player.SearchController),
                        EftJsonConverters.Converters
                    ),
                }
            );
        }
        return result;
    }

    internal async Task RestoreAsync(Checkpoint saved, IReadOnlyDictionary<string, string> identities, CancellationToken token)
    {
        _pausedAt = Clock;
        _paused = true;
        _pausedPatrols = saved.Patrols;
        _native.RestoreObjectBaseline(saved.BaselineItems);
        foreach (var actor in saved.Bots)
        {
            if (!identities.TryGetValue(actor.ProfileId, out var fresh))
                throw new InvalidOperationException("Missing server-authorized checkpoint actor.");
            var descriptor = JsonConvert.DeserializeObject<ProfileDescriptor>(actor.ProfileJson, EftJsonConverters.Converters)!;
            descriptor.Id = fresh;
            var profile = new Profile(descriptor);
            var point = actor.Spawn;
            var location = actor.Health.Position;
            var target = new SpatialVector
            {
                X = location.x,
                Y = location.y,
                Z = location.z,
            };
            var bot = await _native.SpawnAsync(profile, point, actor.EncounterId, actor.Squad, token, target, actor.Brain);
            token.ThrowIfCancellationRequested();
            RetryGuard?.Track(bot.GetPlayer);
            if (!_navigation.IsOnNavMesh(target))
                throw new InvalidOperationException("Checkpoint bot position is no longer navigable.");
            actor.Health.Restore(bot.GetPlayer, actor.OriginalBot, bot);
            if (actor.PlayerWasEnemy)
            {
                bot.BotsGroup.AddEnemy(_player, EBotEnemyCause.initial);
                if (!bot.BotsGroup.IsEnemy(_player))
                    throw new InvalidOperationException("Checkpoint bot hostility to the player could not be restored.");
            }
            var encounter = _encounters.AsValueEnumerable().First(e => e.Encounter.Id == actor.EncounterId);
            var record = new BotRecord
            {
                Bot = bot,
                Player = bot.GetPlayer,
                Health = bot.GetPlayer.ActiveHealthController,
                ProfileId = fresh,
                Encounter = encounter,
                Wave = actor.Wave,
                RosterId = actor.RosterId,
                Squad = actor.Squad,
                Route = actor.Route,
                Spawn = actor.Spawn,
            };
            record.Health.DiedEvent += record.OnDeath;
            _bots.Add(record);
            _ai!.Add(bot, actor.Squad, actor.Route, actor.Spawn);
        }
        foreach (var checkpoint in saved.Encounters)
        {
            var encounter = _encounters.AsValueEnumerable().First(e => e.Encounter.Id == checkpoint.EncounterId);
            encounter.Restore(checkpoint, Clock, identities);
            foreach (var wave in encounter.Waves)
                if (wave.Status == EncounterWaveStatus.Completed)
                    _notifiedWaves.Add(wave.WaveId);
            if (encounter.Waves.AsValueEnumerable().All(w => w.Status == EncounterWaveStatus.Completed))
                _notifiedEncounters.Add(checkpoint.EncounterId);
        }
        _ai!.RestorePatrols(saved.Patrols);
    }

    internal static string CompatibilityError => EncounterNative.CompatibilityError;
    internal string Status { get; private set; } = "Preparing AI preview";
    internal string? Failure { get; private set; }
    internal event Action<MissionSignal>? Signal;
    internal event Action<MissionActor>? ActorRegistered;
    private readonly HashSet<string> _notifiedWaves = new();
    private readonly HashSet<string> _notifiedEncounters = new();

    internal List<string> Occupants(MapVolume volume) =>
        _bots
            .AsValueEnumerable()
            .Where(r =>
                !r.Finished
                && r.Encounter.Waves[r.Wave].Status == EncounterWaveStatus.Active
                && !r.DeathConfirmed
                && r.Player
                && r.Health.IsAlive
                && EncounterNavigation.Contains(volume, r.Player.Transform.position)
            )
            .Select(r => r.ProfileId)
            .ToList();

    internal void ActivateEncounter(string id)
    {
        var encounter = _encounters.AsValueEnumerable().FirstOrDefault(e => e.Encounter.Id == id);
        if (encounter?.Encounter.Trigger.Type == MapEncounterTrigger.Event)
            encounter.TryActivate("event:" + encounter.Encounter.Trigger.EventId, Clock);
    }

    internal async Task BeginAsync(
        EncounterRuntimeContext context,
        MapLayout layout,
        Player player,
        bool observe,
        CancellationToken token,
        string encounterToken = ""
    )
    {
        using var loading = UI.NativeLoadingStatus.Begin("Preparing campaign AI…");
        if (_lifetime != null || _ended)
            throw new InvalidOperationException("Create a fresh encounter runtime for every preview.");
        if (!context.HasIdentity || (!context.IsPreview && context.Mode != EncounterRuntimeModes.Mission))
            throw new InvalidOperationException("Encounter spawning requires a server-confirmed mission or editor preview context.");
        if (!context.IsPreview && !context.PublishedLayoutConfirmed)
            throw new InvalidOperationException("Mission spawning requires a server-confirmed published-layout activation.");
        if (!context.IsPreview && string.IsNullOrWhiteSpace(encounterToken))
            throw new InvalidOperationException("Mission spawning requires a server-issued encounter token.");
        _context = context;
        _layout = layout;
        _player = player;
        _encounterToken = encounterToken;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Failure = null;
        var lifetime = _lifetime;
        foreach (var point in layout.SpawnPoints)
            _anchors.Add(point.Id, point);
        foreach (var encounter in layout.Encounters)
            _encounters.Add(new(encounter));
        await _native.BeginAsync(context, layout, player, observe, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        _ai = EncounterCompatibility.Create(layout);
        _ready = true;
        Status = "AI preview ready";
    }

    internal void MissionStart()
    {
        if (!_ready || _ended)
            return;
        foreach (var state in _encounters)
            if (state.Encounter.Trigger.Type == MapEncounterTrigger.MissionStart)
                state.TryActivate("start", Clock);
        RefreshStatus();
    }

    internal void Event(string eventId)
    {
        if (!_ready || _ended || string.IsNullOrWhiteSpace(eventId))
            return;
        foreach (var state in _encounters)
        {
            var trigger = state.Encounter.Trigger;
            if (trigger.Type == MapEncounterTrigger.Event && trigger.EventId == eventId)
                state.TryActivate("event:" + eventId, Clock);
        }
        RefreshStatus();
    }

    internal void SimulateEntry(string encounterId)
    {
        if (!_ready || _ended)
            return;
        foreach (var state in _encounters)
            if (state.Encounter.Id == encounterId && state.Encounter.Trigger.Type == MapEncounterTrigger.PlayerEntry)
                state.TryActivate("entry", Clock);
        RefreshStatus();
    }

    internal void Tick(bool settling = false)
    {
        if (!_ready || _ended || Failure != null || (_paused && !settling))
            return;
        _lifetime!.Token.ThrowIfCancellationRequested();
        if (!_player)
            throw new InvalidOperationException("The preview player is no longer available.");
        if (!_paused)
            _ai?.Tick();
        if (Clock >= _nextBotTrace)
        {
            _nextBotTrace = Clock + 5;
            foreach (var record in _bots)
                if (!record.Finished && !record.DeathConfirmed && record.Bot)
                    Plugin.LogInfo("AI movement: bot=" + record.ProfileId + "; " + _ai!.Describe(record.Bot));
        }
        if (!settling && Clock < _nextUpdate)
            return;
        _nextUpdate = Clock + .1f;
        var now = Clock;
        foreach (var record in _bots)
        {
            if (record.Finished || record.Encounter.Waves[record.Wave].Status != EncounterWaveStatus.Active)
                continue;
            if (record.DeathConfirmed || record.Health.IsAlive == false)
            {
                record.Encounter.MarkBotDefeated(record.Wave, record.ProfileId, now);
                record.Finished = true;
                Signal?.Invoke(new MissionSignal { Kind = MissionSignals.Death, ProfileId = record.ProfileId });
            }
            else if (!record.Player || !record.Bot)
            {
                record.Encounter.MarkBotFailed(record.Wave, record.ProfileId, "A registered bot disappeared without a confirmed death.");
                record.Finished = true;
            }
        }
        foreach (var state in _encounters)
        {
            foreach (var wave in state.Waves)
                if (wave.Status == EncounterWaveStatus.Completed && _notifiedWaves.Add(wave.WaveId))
                    Signal?.Invoke(new MissionSignal { Kind = MissionSignals.Wave, TargetId = wave.WaveId });
            if (
                state.Waves.Count > 0
                && state.Waves.AsValueEnumerable().All(w => w.Status == EncounterWaveStatus.Completed)
                && _notifiedEncounters.Add(state.Encounter.Id)
            )
                Signal?.Invoke(new MissionSignal { Kind = MissionSignals.Encounter, TargetId = state.Encounter.Id });
            var trigger = state.Encounter.Trigger;
            if (!state.IsActivated && trigger.Type == MapEncounterTrigger.PlayerEntry)
            {
                var volume =
                    trigger.Volume
                    ?? _layout.Checkpoints.AsValueEnumerable().FirstOrDefault(p => p.Id == trigger.ZoneId)
                    ?? (_layout.Exit?.Id == trigger.ZoneId ? _layout.Exit : null);
                if (volume != null && EncounterNavigation.Contains(volume, _player.Transform.position))
                    state.TryActivate("entry", now);
            }
            if (!_paused)
                foreach (var wave in state.ReadyWaves(now))
                    if (Failure == null)
                        _work.Add(SpawnWave(state, wave));
        }
        _work.RemoveAll(t => t.IsCompleted);
        var failed = _encounters.AsValueEnumerable().FirstOrDefault(e => e.IsFailed);
        Failure ??=
            failed == null
                ? null
                : failed.Encounter.Name
                    + ": failed · "
                    + failed.Waves.AsValueEnumerable().First(w => w.Status == EncounterWaveStatus.Failed).FailureReason;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (Failure != null)
        {
            Status = Failure + " · Reset preview to retry";
            return;
        }
        if (_encounters.Count == 0)
        {
            Status = "No encounters in this layout. Reset preview to add one.";
            return;
        }
        var details = new List<string>();
        foreach (var state in _encounters)
        {
            var name = state.Encounter.Name;
            if (!state.IsActivated)
            {
                var trigger = state.Encounter.Trigger;
                details.Add(
                    name
                        + (
                            trigger.Type == MapEncounterTrigger.Event
                                ? " · Waiting for event: " + trigger.EventId + ". Select its encounter, wave or roster and use Simulate."
                            : trigger.Type == MapEncounterTrigger.PlayerEntry
                                ? " · Waiting for player entry. Select its encounter, wave or roster and use Simulate."
                            : " · Waiting for mission start."
                        )
                );
            }
            else if (state.Waves.AsValueEnumerable().Any(w => w.Status is EncounterWaveStatus.Generating or EncounterWaveStatus.Ready))
                details.Add(name + " · Spawning bots…");
            else if (state.Waves.AsValueEnumerable().All(w => w.Status == EncounterWaveStatus.Completed))
                details.Add(name + " · Complete. Reset preview to run again.");
            else if (state.Waves.AsValueEnumerable().Any(w => w.Status == EncounterWaveStatus.Pending))
                details.Add(name + " · Waiting for wave delay or previous wave.");
        }
        Status =
            "AI preview · "
            + _bots.AsValueEnumerable().Count(b => !b.Finished)
            + " bots · "
            + (_ai?.Status ?? "No patrols")
            + (details.Count == 0 ? "" : "\n" + string.Join("\n", details));
    }

    private async Task SpawnWave(EncounterWaveStateMachine state, int waveIndex)
    {
        var generation = Guid.NewGuid().ToString("N");
        if (!state.TryBeginGeneration(waveIndex, generation))
            return;
        var token = _lifetime!.Token;
        var reserved = new List<SpatialCapture>();
        var cleanup = new EncounterWaveCleanup();
        var staged = new List<BotRecord>();
        try
        {
            var wave = state.Encounter.Waves[waveIndex];
            // Reserve every position before requesting profiles. Concurrent triggers cannot overlap.
            foreach (var roster in wave.Roster)
            foreach (var id in roster.SpawnPointIds.AsValueEnumerable().Take(roster.Count))
            {
                if (!_anchors.TryGetValue(id, out var point))
                    throw new InvalidOperationException("An assigned spawn point is missing.");
                var position = EncounterNavigation.ToVector3(point.Position);
                if (
                    _pendingAnchors
                        .AsValueEnumerable()
                        .Any(other => (EncounterNavigation.ToVector3(other.Position) - position).sqrMagnitude < .64f)
                )
                    throw new InvalidOperationException("Spawn assignments overlap another reserved position.");
                CheckPosition(point);
                reserved.Add(point);
                _pendingAnchors.Add(point);
            }
            if (reserved.Count != wave.Roster.AsValueEnumerable().Sum(r => r.Count))
                throw new InvalidOperationException("The wave does not have enough distinct authored spawn positions.");
            foreach (var roster in wave.Roster)
            {
                var profiles = new List<Profile>();
                while (profiles.Count < roster.Count)
                {
                    token.ThrowIfCancellationRequested();
                    var count = Math.Min(16, roster.Count - profiles.Count);
                    var body =
                        _context.Mode == EncounterRuntimeModes.Mission
                            ? JsonConvert.SerializeObject(
                                new MissionEncounterProfilesRequest
                                {
                                    Version = 3,
                                    SeasonId = MissionClient.SeasonId,
                                    CharacterId = MissionClient.CharacterId,
                                    RunId = _context.PreviewGeneration,
                                    AttemptGeneration = _context.AttemptGeneration,
                                    RaidId = _context.RaidId,
                                    EncounterToken = _encounterToken,
                                    EncounterId = state.Encounter.Id,
                                    WaveId = wave.Id,
                                    RosterId = roster.Id,
                                    Offset = profiles.Count,
                                    Count = count,
                                }
                            )
                            : JsonConvert.SerializeObject(
                                new EditorEncounterProfilesRequest
                                {
                                    SessionId = _context.SessionId,
                                    LayoutId = _layout.Id,
                                    Role = roster.Role,
                                    Difficulty = roster.Difficulty,
                                    Count = count,
                                }
                            );
                    var response = JsonConvert.DeserializeObject<EditorEncounterProfilesResponse>(
                        await EncounterRecovery.RequestAsync(
                            () =>
                                RequestHandler.PostJsonAsync(
                                    _context.Mode == EncounterRuntimeModes.Mission
                                        ? "/wtt-campaigns/missions/encounter-profiles"
                                        : "/wtt-campaigns/editor/encounter-profiles",
                                    body
                                ),
                            token,
                            attempt => Plugin.LogInfo($"Encounter '{state.Encounter.Name}' profile transport retry {attempt}/3"),
                            (milliseconds, cancellation) =>
                                UniTask.Delay(milliseconds, delayType: DelayType.Realtime, cancellationToken: cancellation).AsTask()
                        )
                    );
                    token.ThrowIfCancellationRequested();
                    if (response == null || !string.IsNullOrEmpty(response.Error))
                        throw new InvalidOperationException(response?.Error ?? "Bot generation returned no response.");
                    // The native /client/game/bot/generate path deserializes ProfileDescriptor[]
                    // and constructs Profile instances explicitly. Deserializing Profile[] here
                    // makes Json.NET invoke Profile(ProfileDescriptor) with a null descriptor.
                    var descriptors = JsonConvert.DeserializeObject<ProfileDescriptor[]>(
                        response.ProfilesJson,
                        EftJsonConverters.Converters
                    );
                    if (
                        descriptors == null
                        || descriptors.Length != count
                        || descriptors.AsValueEnumerable().Any(d => d == null || d.Info == null)
                    )
                        throw new InvalidOperationException("Bot generation returned an incomplete roster.");
                    var batch = descriptors.AsValueEnumerable().Select(d => new Profile(d)).ToArray();
                    if (
                        !Enum.TryParse<WildSpawnType>(roster.Role, out var expectedRole)
                        || batch.AsValueEnumerable().Any(p => p.Info?.Settings?.Role != expectedRole)
                    )
                        throw new InvalidOperationException("The installed generator changed the requested bot role.");
                    profiles.AddRange(batch);
                }
                var squad = state.Encounter.Id + ":" + (roster.SquadId.Length > 0 ? "squad:" + roster.SquadId : "roster:" + roster.Id);
                for (var index = 0; index < profiles.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var profile = profiles[index];
                    var point = _anchors[roster.SpawnPointIds[index]];
                    CheckPosition(point);
                    if (!state.TryReserveBot(waveIndex, generation, roster.Id, profile.Id, point.Id, out var error))
                        throw new InvalidOperationException(error);
                    cleanup.Track(() => _native.RemoveProfile(profile.Id));
                    var bot = await _native.SpawnAsync(profile, point, state.Encounter.Id, squad, token);
                    token.ThrowIfCancellationRequested();
                    if (!bot || !bot.GetPlayer)
                        throw new InvalidOperationException("Native bot activation did not finish.");
                    var record = new BotRecord
                    {
                        Bot = bot,
                        Player = bot.GetPlayer,
                        ProfileId = profile.Id,
                        Encounter = state,
                        Wave = waveIndex,
                        RosterId = roster.Id,
                        Squad = squad,
                        Route = roster.PatrolRouteId,
                        Spawn = point,
                        Health =
                            bot.GetPlayer.ActiveHealthController
                            ?? throw new InvalidOperationException("Native bot health was not initialized."),
                    };
                    record.DeathConfirmed = !record.Health.IsAlive;
                    record.Health.DiedEvent += record.OnDeath;
                    _bots.Add(record);
                    staged.Add(record);
                    cleanup.Track(() =>
                    {
                        record.Health.DiedEvent -= record.OnDeath;
                        _bots.Remove(record);
                        _ai?.Remove(bot);
                    });
                    RetryGuard?.Track(record.Player);
                    _ai!.Add(bot, squad, roster.PatrolRouteId, point);
                }
            }
            if (!state.TryCommitGeneration(waveIndex, generation, Clock, out var failure))
                throw new InvalidOperationException(failure);
            cleanup.Commit();
            // Publish only a complete wave: a partial native failure must not create mission actors or spawn events.
            foreach (var record in staged)
            {
                ActorRegistered?.Invoke(
                    new MissionActor
                    {
                        ProfileId = record.ProfileId,
                        EncounterId = state.Encounter.Id,
                        WaveId = wave.Id,
                        RosterId = record.RosterId,
                        SquadId = wave.Roster.AsValueEnumerable().First(r => r.Id == record.RosterId).SquadId,
                    }
                );
                Signal?.Invoke(new MissionSignal { Kind = MissionSignals.Spawn, ProfileId = record.ProfileId });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            state.Cancel();
        }
        catch (Exception error)
        {
            var message = error.GetBaseException().Message;
            state.FailGeneration(waveIndex, generation, string.IsNullOrWhiteSpace(message) ? error.Message : message);
            Failure = state.Encounter.Name + ": " + message;
            Plugin.Error(error);
        }
        finally
        {
            try
            {
                cleanup.Rollback();
            }
            catch (Exception error)
            {
                Failure = error.Message;
                Plugin.Error(error);
            }
            foreach (var point in reserved)
                _pendingAnchors.Remove(point);
        }
    }

    private void CheckPosition(SpatialCapture point)
    {
        if (!_navigation.IsOnNavMesh(point.Position) || !_navigation.HasStandingClearance(point.Position))
            throw new InvalidOperationException("Spawn point is blocked or off the NavMesh: " + point.Name);
    }

    internal void StopWork()
    {
        _paused = true;
        foreach (var record in _bots)
            RetryGuard?.Track(record.Player);
        _lifetime?.Cancel();
    }

    internal Task WaitForWorkAsync() => Task.WhenAll(_work.AsValueEnumerable().ToArray());

    internal void Reset(bool preserveWorld = false)
    {
        _ended = true;
        _ready = false;
        _lifetime?.Cancel();
        foreach (var encounter in _encounters)
            encounter.Cancel();
        try
        {
            _native.Reset(preserveWorld);
        }
        finally
        {
            _ai?.Reset();
        }
        _ai = null;
        _encounterToken = "";
        foreach (var record in _bots)
            record.Health.DiedEvent -= record.OnDeath;
        _bots.Clear();
        _pendingAnchors.Clear();
        _lifetime?.Dispose();
        _lifetime = null;
        Status = "Preview reset";
    }
}
