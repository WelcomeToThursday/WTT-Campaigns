using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.Jobs;
using EFT;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Encounters;

/// <summary>
/// Owns the narrow bridge from an authored profile and position to SPT's native bot creator.
/// The coordinator owns encounter and wave state; this class owns the native admission token,
/// registration balance, late callback disposal and preview bot cleanup.
/// </summary>
internal sealed class EncounterNative
{
    private const int NativeReadyAttempts = 240;
    private const int NativeReadyDelayMilliseconds = 50;
    private const int SpawnCallbackTimeoutMilliseconds = 20000;
    private const float SameAnchorDistance = 0.75f;

    private sealed class Activation
    {
        internal Activation(
            EncounterRuntimeContext context,
            EncounterSpawnReservation reservation,
            Profile profile,
            SpatialCapture spawn,
            BotCreationData data,
            BotZone zone,
            int generation,
            string squadId
        )
        {
            Context = context;
            Reservation = reservation;
            Profile = profile;
            Spawn = spawn;
            Data = data;
            Zone = zone;
            Generation = generation;
            SquadId = squadId;
            Stage = ActivationStage.Issued;
        }

        internal EncounterRuntimeContext Context { get; }
        internal EncounterSpawnReservation Reservation { get; }
        internal Profile Profile { get; }
        internal SpatialCapture Spawn { get; }
        internal BotCreationData Data { get; }
        internal BotZone Zone { get; }
        internal int Generation { get; }
        internal string SquadId { get; }
        internal ActivationStage Stage { get; set; }
        internal BotOwner? Owner { get; set; }
        internal BotsGroup? Group { get; set; }
        internal bool CounterEntered { get; set; }
        internal bool CallbackCompleted { get; set; }
        internal bool Cancelled { get; set; }
    }

    private enum ActivationStage
    {
        Issued,
        Creating,
        Registered,
        Admitted,
        Failed,
        Cancelled,
    }

    private sealed class OwnedBot
    {
        internal OwnedBot(Activation activation, BotOwner owner)
        {
            Activation = activation;
            Owner = owner;
        }

        internal Activation Activation { get; }
        internal BotOwner Owner { get; }
        internal bool Removed;
    }

    private static readonly object InstallGate = new();
    private static bool _installed;
    private static string _compatibilityError = "";

    private readonly object _gate = new();
    private readonly List<Activation> _pending = new();
    private readonly Dictionary<string, Activation> _activations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedBot> _ownedByProfile = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedBot> _ownedBySpawn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BotsGroup> _groupsBySquad = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _profileGroups = new(StringComparer.Ordinal);
    private readonly EncounterSpawnAdmission _admission = new();

    private EncounterRuntimeContext? _context;
    private MapLayout? _layout;
    private Player? _observePlayer;
    private BotsController? _botsController;
    private BotSpawner? _spawner;
    private IBotCreator? _creator;
    private int _generation;
    private bool _active;
    private bool _resetting;
    private EncounterPreviewObjects? _objects;

    internal HashSet<string> CaptureObjectBaseline() => _objects?.CaptureBaseline() ?? new();

    internal void RestoreObjectBaseline(IEnumerable<string> ids) => _objects?.RestoreBaseline(ids);

    private EncounterCorePoints? _corePoints;
    private BotCreatorClient? _rendererCreator;
    private readonly HashSet<Player> _existingRendererPlayers = new();

    internal static string CompatibilityError
    {
        get
        {
            lock (InstallGate)
            {
                return _compatibilityError;
            }
        }
    }

    internal bool Active
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    internal EncounterSpawnAdmission Admission => _admission;

    /// <summary>
    /// Installs the optional-mod compatibility and native spawn admission hooks once.  Missing
    /// optional mods disable AI previews while leaving ordinary raids untouched.
    /// </summary>
    internal static void Install()
    {
        lock (InstallGate)
        {
            if (_installed)
                return;

            try
            {
                if (!EncounterCompatibility.Ensure(out var compatibilityError))
                    throw new InvalidOperationException(compatibilityError);
                if (!EncounterSpawnAdmissionGate.Install(out var admissionError))
                    throw new InvalidOperationException(admissionError);
                _compatibilityError = "";
            }
            catch (Exception exception)
            {
                _compatibilityError = exception.Message;
            }

            _installed = true;
        }
    }

    /// <summary>Waits for SPT's native bot controller, creator and spawn system before preview starts.</summary>
    internal async Task BeginAsync(
        EncounterRuntimeContext context,
        MapLayout layout,
        Player player,
        bool observe,
        CancellationToken cancellationToken
    )
    {
        if (context == null || !context.HasIdentity)
            throw new InvalidOperationException("Encounter runtime identity is incomplete.");
        if (!context.IsPreview && !(context.Mode == EncounterRuntimeModes.Mission && context.PublishedLayoutConfirmed))
            throw new InvalidOperationException("Encounter spawning requires preview mode or a published mission layout.");
        if (layout == null)
            throw new InvalidOperationException("An applied map layout is required for encounter preview.");
        if (player == null)
            throw new InvalidOperationException("The editor player is unavailable.");

        Install();
        if (CompatibilityError.Length > 0)
            throw new InvalidOperationException(CompatibilityError);

        var navigationErrors = MapEncounterRules.Errors(layout, new EncounterNavigation(), requireNavigation: true, requireComplete: true);
        if (navigationErrors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", navigationErrors));
        if (!_admission.CanActivate(context, out var admissionError))
            throw new InvalidOperationException(admissionError);

        Reset();
        _objects = new EncounterPreviewObjects();
        lock (_gate)
        {
            _generation++;
            _resetting = false;
            _context = context;
            _layout = layout;
            _observePlayer = observe ? player : null;
        }
        EncounterSpawnAdmissionGate.SetObserveTarget(_observePlayer);

        try
        {
            await WaitForNativeReady(context, cancellationToken);
            _corePoints = new EncounterCorePoints();
            _corePoints.Prepare(context, layout);
            ValidateNativeSpawns(layout);
            lock (_gate)
            {
                if (_resetting || _context == null || !_context.Matches(context))
                    throw new OperationCanceledException(cancellationToken);
                _active = true;
            }
        }
        catch
        {
            Reset();
            throw;
        }
    }

    /// <summary>
    /// Activates exactly one already-generated profile at one authored point.  Native creator
    /// registration is completed before this task returns, and all cancellation paths retain a
    /// stale callback record so a late LocalPlayer/BotOwner is disposed instead of admitted.
    /// </summary>
    internal async Task<BotOwner> SpawnAsync(
        Profile profile,
        SpatialCapture spawn,
        string encounterId,
        string squadId,
        CancellationToken cancellationToken,
        SpatialVector? checkpointPosition = null,
        WildSpawnType? checkpointBrain = null
    )
    {
        if (profile == null)
            throw new InvalidOperationException("A generated bot profile is required.");
        if (spawn == null || string.IsNullOrWhiteSpace(spawn.Id))
            throw new InvalidOperationException("An authored spawn point is required.");

        EncounterRuntimeContext context;
        MapLayout layout;
        BotSpawner spawner;
        IBotCreator creator;
        int generation;
        lock (_gate)
        {
            if (!_active || _context == null || _layout == null || _spawner == null || _creator == null || _resetting)
                throw new InvalidOperationException("The encounter native runtime is not active.");
            context = _context;
            layout = _layout;
            spawner = _spawner;
            creator = _creator;
            generation = _generation;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_admission.CanActivate(context, out var admissionError))
            throw new InvalidOperationException(admissionError);
        if (!TryGetAuthoredSpawn(layout, spawn, out var authoredSpawn))
            throw new InvalidOperationException("The spawn point is not part of the applied layout.");
        if (checkpointPosition != null)
        {
            if (!WTT.Campaigns.Client.Missions.MissionRetryGuard.RestoringActors || context.AttemptGeneration <= 1)
                throw new InvalidOperationException("A checkpoint actor can only be recreated during a frozen retry.");
            authoredSpawn = Newtonsoft.Json.JsonConvert.DeserializeObject<SpatialCapture>(
                Newtonsoft.Json.JsonConvert.SerializeObject(authoredSpawn)
            )!;
            authoredSpawn.Position = checkpointPosition;
        }
        if (checkpointBrain.HasValue && checkpointPosition == null)
            throw new InvalidOperationException("A saved brain requires a validated checkpoint restore.");
        if (!EncounterNavigation.TryToVector(authoredSpawn.Position, out var worldPosition))
            throw new InvalidOperationException("The authored spawn point has no finite position.");

        var navigation = new EncounterNavigation();
        if (!navigation.IsOnNavMesh(authoredSpawn.Position) || !navigation.HasStandingClearance(authoredSpawn.Position))
            throw new InvalidOperationException("The authored spawn point is not a valid standing NavMesh position.");
        if (IsAnchorOccupied(authoredSpawn.Id, worldPosition))
            throw new InvalidOperationException("The authored spawn point is occupied by another encounter bot.");

        if (!TryResolveNativeSpawn(spawner, worldPosition, out var zone, out var corePointId, out var nativeError))
            throw new InvalidOperationException($"Spawn '{authoredSpawn.Name}' ({authoredSpawn.Id}): {nativeError}");
        if (!TryValidateProfile(profile, out var profileError))
            throw new InvalidOperationException(profileError);
        var authoredSquad = string.IsNullOrWhiteSpace(squadId) ? encounterId + ":profile:" + profile.Id : squadId;
        if (!_profileGroups.TryGetValue(authoredSquad, out var profileGroup))
            _profileGroups.Add(authoredSquad, profileGroup = Guid.NewGuid().ToString("N"));
        profile.Info.GroupId = profileGroup;

        if (
            !_admission.TryReserve(context, encounterId, profile.Id, authoredSpawn.Id, out var reservation, out var reservationError)
            || reservation == null
        )
            throw new InvalidOperationException(reservationError);

        var profileData = new GetProfileDataParams(
            profile.Info.Side,
            profile.Info.Settings.Role,
            profile.Info.Settings.BotDifficulty,
            0f,
            // MoreBots reads Id_spawn without null checks. An empty ID denotes an
            // ordinary spawn and cannot opt an authored encounter into hunt behavior.
            new BotSpawnParams { Id_spawn = "" },
            false
        );
        var data = BotCreationData.CreateWithoutProfile(profileData);
        data.AddProfile(profile);
        data.AddPosition(worldPosition, corePointId);
        var activation = new Activation(context, reservation, profile, authoredSpawn, data, zone, generation, authoredSquad)
        {
            Stage = ActivationStage.Issued,
        };
        lock (_gate)
        {
            if (_resetting || generation != _generation || _context == null || !_context.Matches(context))
            {
                activation.Stage = ActivationStage.Cancelled;
                data.StopSpawn();
                _admission.Cancel(context, reservation);
                throw new OperationCanceledException(cancellationToken);
            }

            _pending.Add(activation);
            _activations[reservation.Token] = activation;
            activation.Stage = ActivationStage.Creating;
        }

        IDisposable? lease = null;
        try
        {
            // Generated profiles bypass the ordinary bot cache's asset preparation.
            // Include native body, voice and equipment resources before creating a player.
            await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(
                ObjectsFactory.PoolsCategory.Raid,
                ObjectsFactory.AssemblyType.Local,
                profile.GetAllPrefabPaths().AsValueEnumerable().ToArray(),
                JobYieldPriority.Immediate,
                null,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !navigation.IsOnNavMesh(authoredSpawn.Position)
                || !navigation.HasStandingClearance(authoredSpawn.Position)
                || IsAnchorOccupied(authoredSpawn.Id, worldPosition)
            )
                throw new InvalidOperationException("The authored spawn point became blocked while loading the bot assets.");
            if (!EncounterSpawnAdmissionGate.TryOpen(context, _admission, reservation, worldPosition, out lease, out var scopeError))
                throw new InvalidOperationException(scopeError);

            // The data overload awaits CreateBot and preserves the native profile/lifecycle path.
            // Do not pass the preview CTS into native CreateBot: if cancellation occurs after the
            // LocalPlayer is constructed, SPT's own cancellation branch skips its callback and
            // cannot dispose that player.  The stale callback guard below disposes late results.
            var callback = new TaskCompletionSource<BotOwner>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopwatch = Stopwatch.StartNew();
            spawner._inSpawnProcess++;
            activation.CounterEntered = true;
            using var brainChoice = new EncounterBrainChoice(profile.Id, checkpointBrain);
            var nativeTask = creator.ActivateBot(
                data,
                zone,
                shallBeGroup: false,
                (bot, botZone) => ResolveNativeGroup(activation, spawner, bot, botZone),
                bot => OnNativeCreated(activation, callback, spawner, stopwatch, bot),
                CancellationToken.None
            );

            var completed = await AwaitNativeCreation(nativeTask, callback.Task, cancellationToken);
            if (completed == null)
                throw new InvalidOperationException("The native bot creator completed without a BotOwner.");
            await AwaitNativeActivation(activation, completed, cancellationToken);
            return completed;
        }
        catch (OperationCanceledException)
        {
            activation.Cancelled = true;
            activation.Stage = ActivationStage.Cancelled;
            throw;
        }
        catch (Exception exception)
        {
            activation.Cancelled = true;
            activation.Stage = ActivationStage.Failed;
            throw new InvalidOperationException("Native encounter bot activation failed: " + exception.Message, exception);
        }
        finally
        {
            lease?.Dispose();
            try
            {
                // BotCreationData owns the native event/cancellation registration.  Stop it
                // after the callback has completed as well as on failure so a successful
                // activation cannot retain a profile listener into the next preview.
                activation.Data.StopSpawn();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogError("WTT encounter spawn data cleanup failed: " + exception);
            }
            if (activation.Stage is ActivationStage.Failed or ActivationStage.Cancelled)
            {
                _admission.Cancel(context, reservation);
                if (activation.CounterEntered && !activation.CallbackCompleted)
                {
                    // The data overload normally decrements through ActivateBotCallback.  A
                    // cancellation before callback must balance the counter ourselves.
                    spawner._inSpawnProcess = Math.Max(0, spawner._inSpawnProcess - 1);
                    activation.CounterEntered = false;
                }
            }

            lock (_gate)
            {
                _pending.Remove(activation);
                if (activation.Stage is ActivationStage.Failed or ActivationStage.Cancelled)
                    _activations.Remove(reservation.Token);
            }
        }
    }

    /// <summary>Invalidates admission before stopping native work, then idempotently disposes preview bots.</summary>
    internal void Reset(bool preserveWorld = false)
    {
        EncounterRuntimeContext? context;
        List<Activation> pending;
        List<OwnedBot> owned;
        List<BotsGroup> groups;
        BotSpawner? spawner;
        Player? observePlayer;
        lock (_gate)
        {
            context = _context;
            _generation++;
            _resetting = true;
            _active = false;
            pending = new List<Activation>(_pending);
            owned = new List<OwnedBot>(_ownedByProfile.Values);
            groups = new List<BotsGroup>(_groupsBySquad.Values);
            spawner = _spawner;
            observePlayer = _observePlayer;
        }

        // Invalidation is deliberately the first side effect.  A late creator callback can
        // therefore never consume an old reservation even if native cancellation races it.
        if (context != null)
        {
            EncounterSpawnAdmissionGate.Invalidate(context);
            _admission.Invalidate(context);
        }
        EncounterSpawnAdmissionGate.ClearObserveTarget(observePlayer);

        var pendingFailures = new List<Activation>();
        foreach (var activation in pending)
        {
            activation.Cancelled = true;
            activation.Stage = ActivationStage.Cancelled;
            if (!TryStopSpawn(activation.Data))
                pendingFailures.Add(activation);
        }

        foreach (var bot in owned)
            DisposeOwnedBot(bot, spawner);

        foreach (var group in groups)
            DisposeOwnedGroup(group);

        // A PreActivate callback can create its squad group after the first snapshot but before
        // the cancellation token reaches native code.  Sweep the ledger once more so that late
        // groups cannot survive this Reset call.
        List<BotsGroup> lateGroups;
        lock (_gate)
        {
            lateGroups = new List<BotsGroup>();
            foreach (var group in _groupsBySquad.Values)
            {
                if (!ContainsReference(groups, group))
                    lateGroups.Add(group);
            }
        }
        foreach (var group in lateGroups)
            DisposeOwnedGroup(group);

        Exception? objectFailure = null;
        try
        {
            if (!preserveWorld)
                _objects?.Reset();
            _objects = null;
            if (_rendererCreator != null)
                foreach (var cached in new List<Player>(_rendererCreator._botRenders.Keys))
                    if (!_existingRendererPlayers.Contains(cached))
                        _rendererCreator._botRenders.Remove(cached);
            _rendererCreator = null;
            _existingRendererPlayers.Clear();
        }
        catch (Exception error)
        {
            objectFailure = error;
        }
        bool cleanupPending;
        lock (_gate)
        {
            _pending.Clear();
            _pending.AddRange(pendingFailures);
            _activations.Clear();
            _profileGroups.Clear();
            // DisposeOwnedBot removes successfully torn-down entries.  Keep failures in both
            // ledgers so a repeated Reset can retry native removal instead of orphaning a bot.
            cleanupPending = _pending.Count != 0 || _ownedByProfile.Count != 0 || _groupsBySquad.Count != 0 || objectFailure != null;
            _context = null;
            _layout = null;
            _observePlayer = null;
            _botsController = null;
            _creator = null;
            if (!cleanupPending)
                _spawner = null;
        }
        if (!cleanupPending)
        {
            _corePoints?.Dispose();
            _corePoints = null;
        }
        if (cleanupPending)
            throw new InvalidOperationException("Native preview cleanup remains pending; retry Reset preview.", objectFailure);
    }

    private static bool TryStopSpawn(BotCreationData data)
    {
        try
        {
            data.StopSpawn();
            return true;
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT encounter spawn cancellation failed: " + exception);
            return false;
        }
    }

    private static bool ContainsReference(List<BotsGroup> groups, BotsGroup candidate)
    {
        foreach (var group in groups)
        {
            if (ReferenceEquals(group, candidate))
                return true;
        }

        return false;
    }

    private async Task WaitForNativeReady(EncounterRuntimeContext context, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < NativeReadyAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bots = BotsController.FindBotControllerEditorOnly();
            var spawner = bots?.BotSpawner;
            var creator = spawner?._botCreator;
            if (bots != null && spawner != null && creator != null && spawner._spawnSystem != null && creator.StartProfilesLoaded)
            {
                if (spawner._bots == null)
                    throw new InvalidOperationException("The native bot list is unavailable.");
                if (spawner._bots.Count != 0)
                    throw new InvalidOperationException("AI preview cannot start while unowned native bots are present.");

                lock (_gate)
                {
                    if (_context == null || !_context.Matches(context) || _resetting)
                        throw new OperationCanceledException(cancellationToken);
                    _botsController = bots;
                    _spawner = spawner;
                    _creator = creator;
                    _rendererCreator =
                        creator as BotCreatorClient
                        ?? throw new InvalidOperationException("The installed bot creator is not supported by encounter cleanup.");
                    _existingRendererPlayers.Clear();
                    foreach (var cached in _rendererCreator._botRenders.Keys)
                        _existingRendererPlayers.Add(cached);
                }
                return;
            }

            await UniTask.Delay(NativeReadyDelayMilliseconds, delayType: DelayType.Realtime, cancellationToken: cancellationToken);
        }

        throw new InvalidOperationException("The native bot controller, creator or spawn system did not become ready.");
    }

    private async Task<BotOwner> AwaitNativeCreation(Task nativeTask, Task<BotOwner> callbackTask, CancellationToken cancellationToken)
    {
        var elapsedMilliseconds = 0;
        while (!nativeTask.IsCompleted && !callbackTask.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (elapsedMilliseconds >= SpawnCallbackTimeoutMilliseconds)
                throw new TimeoutException("The native bot creator did not report activation in time.");

            var waitMilliseconds = Math.Min(NativeReadyDelayMilliseconds, SpawnCallbackTimeoutMilliseconds - elapsedMilliseconds);
            await UniTask.Delay(waitMilliseconds, delayType: DelayType.Realtime, cancellationToken: cancellationToken);
            elapsedMilliseconds += waitMilliseconds;
        }

        if (callbackTask.IsCompleted)
            return await callbackTask;

        await nativeTask;
        if (!callbackTask.IsCompleted)
            throw new InvalidOperationException("The native bot creator returned before registering the bot.");

        return await callbackTask;
    }

    private void OnNativeCreated(
        Activation activation,
        TaskCompletionSource<BotOwner> callback,
        BotSpawner spawner,
        Stopwatch stopwatch,
        BotOwner bot
    )
    {
        try
        {
            if (bot == null || !IsCurrent(activation) || activation.Cancelled)
            {
                DisposeLateBot(bot);
                callback.TrySetException(new OperationCanceledException("A stale encounter bot activation was discarded."));
                return;
            }

            if (!ProfileMatches(activation.Profile, bot))
            {
                activation.Stage = ActivationStage.Failed;
                DisposeLateBot(bot);
                callback.TrySetException(new InvalidOperationException("Native activation returned a different profile."));
                return;
            }

            // BotCreatorClient has created BotOwner and run PreActivate, but the native
            // BotSpawner registration/lifecycle callback still belongs to us.  Balance the
            // in-spawn counter exactly as the native spawner does, and invoke OnBotCreated,
            // AfterCreation and SetDieCallback through the installed method.
            activation.Owner = bot;
            WTT.Campaigns.Client.Missions.MissionRetryGuard.TrackMissionBot(bot.GetPlayer);
            var owned = new OwnedBot(activation, bot);
            lock (_gate)
            {
                _ownedByProfile[activation.Profile.Id] = owned;
                _ownedBySpawn[activation.Spawn.Id] = owned;
            }
            try
            {
                spawner.ActivateBotCallback(bot, activation.Data, null, shallBeGroup: false, stopwatch);
                activation.CallbackCompleted = true;
                activation.CounterEntered = false;
                activation.Stage = ActivationStage.Registered;
                if (activation.Group == null)
                    throw new InvalidOperationException("Native squad group was not created before registration.");
            }
            catch (Exception exception)
            {
                activation.Stage = ActivationStage.Failed;
                if (activation.CounterEntered)
                {
                    spawner._inSpawnProcess = Math.Max(0, spawner._inSpawnProcess - 1);
                    activation.CounterEntered = false;
                }
                DisposeOwnedBot(owned, spawner);
                callback.TrySetException(exception);
                return;
            }

            var admissionError = "";
            if (
                !IsCurrent(activation)
                || !_admission.TryConsume(activation.Context, activation.Reservation, out admissionError)
                || !EncounterSpawnAdmissionGate.MarkAdmitted(activation.Context, activation.Reservation)
            )
            {
                activation.Stage = ActivationStage.Failed;
                DisposeOwnedBot(owned, spawner);
                callback.TrySetException(
                    new InvalidOperationException(
                        string.IsNullOrWhiteSpace(admissionError) ? "The encounter admission reservation became stale." : admissionError
                    )
                );
                return;
            }

            activation.Stage = ActivationStage.Admitted;
            lock (_gate)
            {
                if (!IsCurrent(activation))
                {
                    activation.Stage = ActivationStage.Failed;
                }
                else
                {
                    _ownedByProfile[activation.Profile.Id] = owned;
                    _ownedBySpawn[activation.Spawn.Id] = owned;
                }
            }

            if (activation.Stage != ActivationStage.Admitted)
            {
                DisposeOwnedBot(owned, spawner);
                callback.TrySetException(new OperationCanceledException("The encounter generation was reset during activation."));
                return;
            }

            callback.TrySetResult(bot);
        }
        catch (Exception exception)
        {
            activation.Stage = ActivationStage.Failed;
            callback.TrySetException(exception);
        }
    }

    private async Task AwaitNativeActivation(Activation activation, BotOwner bot, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent(activation) || activation.Cancelled)
                throw new OperationCanceledException("The preview was reset while the bot initialized.", cancellationToken);
            if (!bot || !bot.GetPlayer || bot.IsDead || bot.BotState is EBotState.ActiveFail or EBotState.Disposed)
                throw new InvalidOperationException("Native AI initialization failed before the bot became ready.");
            // Registration occurs in PreActive. Native updates initialize tactics,
            // brain and weapon handling afterward; never force those lifecycle calls.
            if (bot.BotState == EBotState.Active && bot.Tactic?.SubTactic != null)
            {
                var group = activation.Group;
                if (group == null || bot.BotsGroup != group)
                    throw new InvalidOperationException("The bot's authored squad changed during initialization.");
                if (!group._members.Contains(bot))
                    group.AddMember(bot);
                group.MakeStrategyDecision();
                return;
            }
            if (watch.ElapsedMilliseconds >= SpawnCallbackTimeoutMilliseconds)
                throw new TimeoutException("The bot registered but its native AI did not become ready in time.");
            await UniTask.Delay(NativeReadyDelayMilliseconds, delayType: DelayType.Realtime, cancellationToken: cancellationToken);
        }
    }

    private bool IsCurrent(Activation activation)
    {
        lock (_gate)
        {
            return _active
                && !_resetting
                && _context != null
                && _context.Matches(activation.Context)
                && activation.Generation == _generation
                && EncounterSpawnAdmissionGate.IsCurrent(activation.Context, activation.Reservation);
        }
    }

    private static bool ProfileMatches(Profile profile, BotOwner bot)
    {
        if (bot == null || bot.GetPlayer == null || profile == null)
            return false;
        return string.Equals(bot.ProfileId, profile.Id, StringComparison.Ordinal)
            && string.Equals(bot.GetPlayer.ProfileId, profile.Id, StringComparison.Ordinal)
            && string.Equals(bot.GetPlayer.Profile.Id, profile.Id, StringComparison.Ordinal);
    }

    private BotsGroup? ResolveNativeGroup(Activation activation, BotSpawner spawner, BotOwner bot, BotZone zone)
    {
        try
        {
            if (!IsCurrent(activation) || !ProfileMatches(activation.Profile, bot))
                return null;

            lock (_gate)
            {
                spawner.SetBotAsEnemy(bot);
                // Native GetGroups selects only the first matching group for a role.
                // Deliver the same relationship notification to every authored squad.
                foreach (var authoredGroup in _groupsBySquad.Values)
                {
                    if (authoredGroup.IsPlayerEnemy(bot))
                        authoredGroup.AddEnemy(bot, EBotEnemyCause.initial);
                    if (authoredGroup.IsAlly(bot))
                        authoredGroup.AddAlly(bot.GetPlayer);
                }
                if (_groupsBySquad.TryGetValue(activation.SquadId, out var existing))
                {
                    activation.Group = existing;
                    return existing;
                }

                // GetGroupAndSetEnemies deliberately joins native role/zone groups.  That would
                // merge separate authored squads, so preview squads receive an isolated native
                // group with the same BotGame and enemy initialization as SPT's group factory.
                var enemies = new List<BotOwner>();
                foreach (var enemy in spawner.GetBotEnemiesList(bot))
                    if (enemy.Profile.Info.GroupId != bot.Profile.Info.GroupId)
                        enemies.Add(enemy);
                var group = new BotsGroup(
                    zone,
                    spawner.BotGame,
                    bot,
                    enemies,
                    spawner._deadBodiesController,
                    spawner._allPlayers,
                    forBoss: false
                );
                // AddNoKey keeps authored squads independent even when two rosters share a
                // native role and zone.  The dictionary owns the group lifecycle and feeds it
                // player/enemy updates; retaining the group only in our local ledger would
                // leave an isolated object that native ticks never visit.
                spawner.Groups.AddNoKey(group, zone);
                _groupsBySquad.Add(activation.SquadId, group);
                activation.Group = group;
                return group;
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT encounter native squad group registration failed: " + exception);
            throw;
        }
    }

    private void DisposeOwnedGroup(BotsGroup group)
    {
        if (group == null)
            return;

        try
        {
            if (!RemoveNativeGroup(group))
                throw new InvalidOperationException("The native squad group is still registered.");

            // BotOwner cleanup runs first.  Clearing this custom group's member list prevents
            // BotsGroup.Dispose from disposing an owner a second time.
            group._members.Clear();
            group.Dispose();
            lock (_gate)
            {
                string? remove = null;
                foreach (var pair in _groupsBySquad)
                {
                    if (ReferenceEquals(pair.Value, group))
                    {
                        remove = pair.Key;
                        break;
                    }
                }

                if (remove != null)
                    _groupsBySquad.Remove(remove);
            }
        }
        catch (Exception exception)
        {
            // Keep the group in the ledger so a later idempotent Reset can retry disposal.
            UnityEngine.Debug.LogError("WTT encounter squad group cleanup remains pending: " + exception);
        }
    }

    private static bool RemoveNativeGroup(BotsGroup group)
    {
        try
        {
            var groups = group.BotGame?.BotsController?.Groups();
            if (groups == null)
                return true;

            foreach (KeyValuePair<BotZone, BotZoneGroups> pair in groups)
            {
                var list = pair.Value._noConnectionGroups;
                while (list.Remove(group)) { }
            }

            return true;
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT encounter native squad group removal failed: " + exception);
            return false;
        }
    }

    internal void RemoveProfile(string profileId)
    {
        if (!_ownedByProfile.TryGetValue(profileId, out var owned))
            return;
        owned.Activation.Cancelled = true;
        owned.Activation.Stage = ActivationStage.Cancelled;
        DisposeOwnedBot(owned, _spawner);
        if (!owned.Removed)
            throw new InvalidOperationException("Native cleanup remains pending for encounter actor " + profileId + ".");
    }

    private void DisposeOwnedBot(OwnedBot owned, BotSpawner? spawner)
    {
        if (owned == null || owned.Removed)
            return;

        if (DisposeOwnedNativeRegistration(owned.Owner, spawner))
        {
            owned.Removed = true;
            lock (_gate)
            {
                if (_ownedByProfile.TryGetValue(owned.Activation.Profile.Id, out var profileOwned) && ReferenceEquals(profileOwned, owned))
                    _ownedByProfile.Remove(owned.Activation.Profile.Id);
                if (_ownedBySpawn.TryGetValue(owned.Activation.Spawn.Id, out var spawnOwned) && ReferenceEquals(spawnOwned, owned))
                    _ownedBySpawn.Remove(owned.Activation.Spawn.Id);
            }
        }
        else
        {
            UnityEngine.Debug.LogError("WTT encounter bot cleanup remains pending for profile " + owned.Activation.Profile.Id + ".");
        }
    }

    private static bool DisposeOwnedNativeRegistration(BotOwner bot, BotSpawner? spawner)
    {
        if (bot == null)
            return true;

        var success = true;

        try
        {
            if (spawner != null && !bot.IsDead)
            {
                // OnBotCreated integrations can throw after _bots.Add but before the
                // activation callback returns. Remove partial native registrations too.
                foreach (var registered in spawner._bots.BotOwners)
                    if (ReferenceEquals(registered, bot))
                    {
                        spawner.BotDied(bot);
                        break;
                    }
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT encounter native bot removal failed: " + exception);
            success = false;
        }

        try
        {
            // BotOwner.Dispose intentionally returns while PreActive.  A cancellation can race
            // immediately after BotCreatorClient.PreActivate, so move that owner through the
            // native failure state before disposal to release brain, mover and event hooks.
            if (bot.BotState == EBotState.PreActive)
                bot.BotState = EBotState.ActiveFail;
            bot.Dispose();
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT encounter BotOwner disposal failed: " + exception);
            success = false;
        }

        try
        {
            var player = bot.GetPlayer;
            if (player != null)
            {
                EncounterPlayerCleanup.Dispose(player);
            }
            else if (bot.gameObject != null)
            {
                EFT.AssetsManager.AssetPoolObject.ReturnToPool(bot.gameObject, true);
            }
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT encounter player disposal failed: " + exception);
            success = false;
        }

        return success;
    }

    private static void DisposeLateBot(BotOwner? bot)
    {
        if (bot == null)
            return;
        _ = DisposeOwnedNativeRegistration(bot, null);
    }

    private bool IsAnchorOccupied(string spawnId, Vector3 position)
    {
        lock (_gate)
        {
            if (_ownedBySpawn.TryGetValue(spawnId, out var owned))
            {
                if (IsAlive(owned.Owner))
                    return true;

                // A dead bot may still own a corpse, dropped equipment or native event
                // subscriptions.  Release only the placement reservation so a later wave can
                // use the authored anchor; keep the profile ledger until Reset disposes it.
                _ownedBySpawn.Remove(spawnId);
            }

            foreach (var candidate in _ownedByProfile.Values)
            {
                if (!IsAlive(candidate.Owner) || candidate.Owner.GetPlayer == null)
                    continue;
                if ((candidate.Owner.GetPlayer.Position - position).sqrMagnitude < SameAnchorDistance * SameAnchorDistance)
                    return true;
            }

            return false;
        }
    }

    private static bool IsAlive(BotOwner owner)
    {
        if (owner == null || owner.IsDead || owner.GetPlayer == null)
            return false;
        try
        {
            return owner.GetPlayer.HealthController?.IsAlive == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetAuthoredSpawn(MapLayout layout, SpatialCapture requested, out SpatialCapture authored)
    {
        authored = null!;
        foreach (var candidate in layout.SpawnPoints ?? new())
        {
            if (candidate == null || !string.Equals(candidate.Id, requested.Id, StringComparison.Ordinal))
                continue;
            if (candidate.Position?.Finite != true || requested.Position?.Finite != true)
                return false;
            var delta = new Vector3(
                candidate.Position.X - requested.Position.X,
                candidate.Position.Y - requested.Position.Y,
                candidate.Position.Z - requested.Position.Z
            );
            if (delta.sqrMagnitude > 0.0001f)
                return false;
            authored = candidate;
            return true;
        }

        return false;
    }

    private void ValidateNativeSpawns(MapLayout layout)
    {
        var assigned = new HashSet<string>();
        foreach (var encounter in layout.Encounters)
        foreach (var wave in encounter.Waves)
        foreach (var roster in wave.Roster)
        foreach (var id in roster.SpawnPointIds)
            assigned.Add(id);
        foreach (var point in layout.SpawnPoints)
        {
            if (!assigned.Contains(point.Id))
                continue;
            if (!TryResolveNativeSpawn(_spawner!, EncounterNavigation.ToVector3(point.Position), out _, out _, out var error))
                throw new InvalidOperationException($"Spawn '{point.Name}' ({point.Id}): {error}");
        }
    }

    private static bool TryResolveNativeSpawn(BotSpawner spawner, Vector3 position, out BotZone zone, out int corePointId, out string error)
    {
        zone = null!;
        corePointId = 0;
        error = "";
        try
        {
            zone = spawner.GetClosestZone(position, out _);
            if (zone == null)
            {
                error = "No native bot zone can own the authored spawn point.";
                return false;
            }

            // The native helper uses a static holder cache. Editor map changes must
            // refresh it before querying connectivity on the currently loaded map.
            var points = AICorePointHolder.GetAllTestObjects(canUseCache: false);
            if (points == null || points.Count == 0)
            {
                error = "The loaded map has no native AI core points available.";
                return false;
            }
            var corePoint = AICorePointHolder.GetAnyPointToConnect(position);
            if (corePoint == null)
            {
                AICorePoint? nearest = null;
                var distance = float.PositiveInfinity;
                foreach (var candidate in points)
                {
                    if (!candidate)
                        continue;
                    var squared = (candidate.Position - position).sqrMagnitude;
                    if (squared >= distance)
                        continue;
                    nearest = candidate;
                    distance = squared;
                }
                var detail = $"Checked {points.Count} native core points at {position.ToString("F2")}.";
                if (nearest)
                {
                    var forward = new NavMeshPath();
                    var reverse = new NavMeshPath();
                    var outward = NavMesh.CalculatePath(position, nearest!.Position, NavMesh.AllAreas, forward);
                    var inward = NavMesh.CalculatePath(nearest.Position, position, NavMesh.AllAreas, reverse);
                    detail +=
                        $" Nearest core {nearest.Id}, {Mathf.Sqrt(distance):F1} m: outward {(outward ? forward.status.ToString() : "no path")}, return {(inward ? reverse.status.ToString() : "no path")}.";
                }
                error =
                    "The spawn is on walkable ground but has no complete two-way route to the map's native AI network. "
                    + "Check for a sealed container/barrier passage or move the spawn to connected ground. "
                    + detail;
                return false;
            }

            corePointId = corePoint.Id;
            return true;
        }
        catch (Exception exception)
        {
            error = "Native spawn placement is unavailable: " + exception.Message;
            return false;
        }
    }

    private static bool TryValidateProfile(Profile profile, out string error)
    {
        error = "";
        if (profile.Info == null || profile.Info.Settings == null)
        {
            error = "Generated profile settings are missing.";
            return false;
        }

        var role = profile.Info.Settings.Role;
        if (role != WildSpawnType.assault && role != WildSpawnType.pmcUSEC && role != WildSpawnType.pmcBEAR)
        {
            error = "The generated profile role is not supported by the installed encounter integration.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            error = "Generated profile identity is missing.";
            return false;
        }

        return true;
    }
}
