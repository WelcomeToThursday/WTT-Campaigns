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
    private EncounterPatrolRuntime? _patrol;
    private CancellationTokenSource? _lifetime;
    private bool _ready,
        _ended;
    private float _nextUpdate;

    internal static string CompatibilityError => EncounterNative.CompatibilityError;
    internal string Status { get; private set; } = "Preparing AI preview";
    internal string? Failure { get; private set; }

    internal async Task BeginAsync(
        EncounterRuntimeContext context,
        MapLayout layout,
        Player player,
        bool observe,
        CancellationToken token,
        string encounterToken = ""
    )
    {
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
        _patrol = new EncounterPatrolRuntime(layout);
        _ready = true;
        Status = "AI preview ready";
    }

    internal void MissionStart()
    {
        if (!_ready || _ended)
            return;
        foreach (var state in _encounters)
            if (state.Encounter.Trigger.Type == MapEncounterTrigger.MissionStart)
                state.TryActivate("start", Time.time);
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
                state.TryActivate("event:" + eventId, Time.time);
        }
        RefreshStatus();
    }

    internal void SimulateEntry(string encounterId)
    {
        if (!_ready || _ended)
            return;
        foreach (var state in _encounters)
            if (state.Encounter.Id == encounterId && state.Encounter.Trigger.Type == MapEncounterTrigger.PlayerEntry)
                state.TryActivate("entry", Time.time);
        RefreshStatus();
    }

    internal void Tick()
    {
        if (!_ready || _ended)
            return;
        _lifetime!.Token.ThrowIfCancellationRequested();
        if (!_player)
            throw new InvalidOperationException("The preview player is no longer available.");
        _patrol?.Tick();
        if (Time.time < _nextUpdate)
            return;
        _nextUpdate = Time.time + .1f;
        var now = Time.time;
        foreach (var record in _bots)
        {
            if (record.Finished || record.Encounter.Waves[record.Wave].Status != EncounterWaveStatus.Active)
                continue;
            if (record.DeathConfirmed || record.Health.IsAlive == false)
            {
                record.Encounter.MarkBotDefeated(record.Wave, record.ProfileId, now);
                record.Finished = true;
            }
            else if (!record.Player || !record.Bot)
            {
                record.Encounter.MarkBotFailed(record.Wave, record.ProfileId, "A registered bot disappeared without a confirmed death.");
                record.Finished = true;
            }
        }
        foreach (var state in _encounters)
        {
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
            foreach (var wave in state.ReadyWaves(now))
                _work.Add(SpawnWave(state, wave));
        }
        _work.RemoveAll(t => t.IsCompleted);
        var failed = _encounters.AsValueEnumerable().FirstOrDefault(e => e.IsFailed);
        Failure =
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
            + (_patrol?.Status ?? "No patrols")
            + (details.Count == 0 ? "" : "\n" + string.Join("\n", details));
    }

    private async Task SpawnWave(EncounterWaveStateMachine state, int waveIndex)
    {
        var generation = Guid.NewGuid().ToString("N");
        if (!state.TryBeginGeneration(waveIndex, generation))
            return;
        var token = _lifetime!.Token;
        var reserved = new List<SpatialCapture>();
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
                                    Version = 1,
                                    SeasonId = MissionClient.SeasonId,
                                    CharacterId = MissionClient.CharacterId,
                                    RunId = _context.PreviewGeneration,
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
                        await RequestHandler.PostJsonAsync(
                            _context.Mode == EncounterRuntimeModes.Mission
                                ? "/wtt-campaigns/missions/encounter-profiles"
                                : "/wtt-campaigns/editor/encounter-profiles",
                            body
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
                        Health =
                            bot.GetPlayer.ActiveHealthController
                            ?? throw new InvalidOperationException("Native bot health was not initialized."),
                    };
                    record.DeathConfirmed = !record.Health.IsAlive;
                    record.Health.DiedEvent += record.OnDeath;
                    _bots.Add(record);
                    if (roster.PatrolRouteId.Length > 0)
                        _patrol!.Add(bot, squad, roster.PatrolRouteId);
                }
            }
            if (!state.TryCommitGeneration(waveIndex, generation, Time.time, out var failure))
                throw new InvalidOperationException(failure);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            state.Cancel();
        }
        catch (Exception error)
        {
            var message = error.GetBaseException().Message;
            state.FailGeneration(waveIndex, generation, string.IsNullOrWhiteSpace(message) ? error.Message : message);
            Plugin.Error(error);
        }
        finally
        {
            foreach (var point in reserved)
                _pendingAnchors.Remove(point);
        }
    }

    private void CheckPosition(SpatialCapture point)
    {
        if (!_navigation.IsOnNavMesh(point.Position) || !_navigation.HasStandingClearance(point.Position))
            throw new InvalidOperationException("Spawn point is blocked or off the NavMesh: " + point.Name);
    }

    internal void Reset()
    {
        _ended = true;
        _ready = false;
        _lifetime?.Cancel();
        foreach (var encounter in _encounters)
            encounter.Cancel();
        try
        {
            _native.Reset();
        }
        finally
        {
            _patrol?.Reset();
        }
        _patrol = null;
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
