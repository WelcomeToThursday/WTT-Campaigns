using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;
using UnityEngine;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

/// <summary>
/// Admission boundary for every editor-owned native bot activation.
///
/// The scope is carried by AsyncLocal so it follows the native creator's asynchronous
/// activation work.  There is no process-wide bypass flag: a creator call is admitted only
/// while the current flow owns the exact reservation issued by the encounter coordinator.
/// </summary>
internal static class EncounterSpawnAdmissionGate
{
    private sealed class ScopeCleanup
    {
        internal BotOwner? Owner;
        internal BotCreatorClient? RendererCreator;
        internal List<Player> Players { get; } = new();
    }

    private sealed class Scope
    {
        internal Scope(EncounterRuntimeContext context, EncounterSpawnReservation reservation, Vector3 expectedPosition)
        {
            Context = context;
            Reservation = reservation;
            ExpectedPosition = expectedPosition;
        }

        private readonly object _gate = new();
        private readonly List<Player> _registeredPlayers = new();
        private Player? _registeredPlayer;
        private BotOwner? _owner;
        private bool _createStarted;
        private bool _ownerCreateStarted;
        private bool _admitted;
        private bool _invalidated;
        private bool _closed;

        internal EncounterRuntimeContext Context { get; }
        internal EncounterSpawnReservation Reservation { get; }
        internal Vector3 ExpectedPosition { get; }
        internal BotCreatorClient? RendererCreator;

        internal bool Closed
        {
            get
            {
                lock (_gate)
                {
                    return _closed;
                }
            }
            set
            {
                lock (_gate)
                {
                    _closed = value;
                }
            }
        }

        internal bool Invalidated
        {
            get
            {
                lock (_gate)
                {
                    return _invalidated;
                }
            }
        }

        internal bool TrackPlayer(Player player)
        {
            if (player == null)
                return false;

            lock (_gate)
            {
                if (_closed || _invalidated)
                    return false;
                if (_registeredPlayer != null && !ReferenceEquals(_registeredPlayer, player))
                    return false;

                _registeredPlayer = player;
                foreach (var existing in _registeredPlayers)
                {
                    if (ReferenceEquals(existing, player))
                        return true;
                }

                _registeredPlayers.Add(player);
                return true;
            }
        }

        internal bool HasPlayer(Player player)
        {
            lock (_gate)
            {
                return !_closed && !_invalidated && ReferenceEquals(_registeredPlayer, player);
            }
        }

        internal bool EnterCreate()
        {
            lock (_gate)
            {
                if (_closed || _invalidated || _createStarted)
                    return false;
                _createStarted = true;
                return true;
            }
        }

        internal bool HasCreateStarted
        {
            get
            {
                lock (_gate)
                {
                    return !_closed && !_invalidated && _createStarted;
                }
            }
        }

        internal bool EnterOwnerCreate()
        {
            lock (_gate)
            {
                if (_closed || _invalidated || _ownerCreateStarted)
                    return false;
                _ownerCreateStarted = true;
                return true;
            }
        }

        internal bool HasBoundOwnerForPlayer(Player player)
        {
            lock (_gate)
            {
                return !_closed && !_invalidated && _owner != null && ReferenceEquals(_owner.GetPlayer, player);
            }
        }

        internal bool BindOwner(BotOwner owner)
        {
            if (owner == null)
                return false;

            lock (_gate)
            {
                if (_closed || _invalidated || _owner != null)
                    return false;
                _owner = owner;
                return true;
            }
        }

        internal bool HasOwner(BotOwner owner)
        {
            lock (_gate)
            {
                return !_closed && !_invalidated && ReferenceEquals(_owner, owner);
            }
        }

        internal bool MarkAdmitted()
        {
            lock (_gate)
            {
                if (_closed || _invalidated || _owner == null)
                    return false;
                _admitted = true;
                return true;
            }
        }

        internal ScopeCleanup Invalidate()
        {
            lock (_gate)
            {
                _invalidated = true;
                _closed = true;
                var cleanup = new ScopeCleanup { RendererCreator = RendererCreator };
                if (!_admitted)
                {
                    cleanup.Owner = _owner;
                    cleanup.Players.AddRange(_registeredPlayers);
                }
                _registeredPlayers.Clear();
                _registeredPlayer = null;
                _owner = null;
                return cleanup;
            }
        }

        internal ScopeCleanup CloseForLease()
        {
            lock (_gate)
            {
                _closed = true;
                var cleanup = new ScopeCleanup { RendererCreator = RendererCreator };
                if (!_admitted)
                {
                    cleanup.Owner = _owner;
                    cleanup.Players.AddRange(_registeredPlayers);
                }
                _registeredPlayers.Clear();
                _registeredPlayer = null;
                _owner = null;
                return cleanup;
            }
        }
    }

    private sealed class Lease : IDisposable
    {
        private readonly Scope _scope;

        internal Lease(Scope scope)
        {
            _scope = scope;
        }

        public void Dispose()
        {
            var cleanup = _scope.CloseForLease();
            if (ReferenceEquals(Current.Value, _scope))
            {
                Current.Value = null;
            }

            lock (Gate)
            {
                Scopes.Remove(_scope.Reservation.Token);
                var contextKey = ContextKey(_scope.Context);
                var contextStillScoped = false;
                foreach (var scope in Scopes.Values)
                {
                    if (Matches(scope.Context, _scope.Context))
                    {
                        contextStillScoped = true;
                        break;
                    }
                }

                if (!contextStillScoped)
                    InvalidatedContexts.Remove(contextKey);
            }

            DisposeScopeCleanup(cleanup);
        }
    }

    private static readonly AsyncLocal<Scope?> Current = new();
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Scope> Scopes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, EncounterRuntimeContext> MissionContexts = new(StringComparer.Ordinal);
    private static readonly HashSet<string> InvalidatedContexts = new(StringComparer.Ordinal);
    private static Harmony? _harmony;
    private static bool _installed;
    private static string _error = "";
    private static Player? _excludedObservePlayer;

    internal static bool CoverageVerified
    {
        get
        {
            lock (Gate)
            {
                return _installed && _error.Length == 0;
            }
        }
    }

    internal static string CoverageError
    {
        get
        {
            lock (Gate)
            {
                return _error;
            }
        }
    }

    /// <summary>
    /// Patches both native BotSpawner routes and BotCreatorClient direct routes.  Ordinary raids
    /// pass through unchanged because the prefixes only gate while EditorMode is active.
    /// </summary>
    internal static bool Install(out string error)
    {
        lock (Gate)
        {
            if (_installed)
            {
                error = _error;
                return _error.Length == 0;
            }

            try
            {
                _harmony = new Harmony("com.wtt.campaigns.encounter-admission");
                var required = new[]
                {
                    (typeof(BotSpawner), "ActivateBotsByWave"),
                    (typeof(BotSpawner), "ActivateBotsWithoutWave"),
                    (typeof(BotSpawner), "SpawnBotBTR"),
                    (typeof(BotSpawner), "SpawnBotByTypeForce"),
                    (typeof(BotSpawner), "TryToSpawnInZoneAndDelay"),
                    (typeof(BotSpawner), "SpawnBotsInZoneOnPositions"),
                    (typeof(BotSpawner), "TrySpawnFreeAndDelay"),
                    (typeof(BotSpawner), "SpawnAndActivateNowDebugClient"),
                    (typeof(BotSpawner), "SpawnAndActivateNowDebugFromLocalFilesServer"),
                    (typeof(BotSpawner), "SpawnAndActivateNowDebugServer"),
                    (typeof(BotSpawner), "DebugSpawnAnyway"),
                    (typeof(BotSpawner), "CheckSpawnOnFreeAfterDelay"),
                    (typeof(BotSpawner), "method_7"),
                    (typeof(BotSpawner), "method_10"),
                    (typeof(BotsController), "ActivateBotsByWave"),
                    (typeof(BotsController), "ActivateBotsWithoutWave"),
                    (typeof(BotsController), "DebugSpawnServerAnyway"),
                    (typeof(BotCreatorClient), "ActivateBot"),
                    (typeof(BotCreatorClient), "CreateBot"),
                    (typeof(BotCreatorClient), "method_0"),
                    (typeof(BotCreatorClient), "method_1"),
                };

                foreach (var (type, name) in required)
                {
                    var methods = GetMethods(type, name);
                    if (methods.Length == 0)
                    {
                        throw new MissingMethodException(type.FullName, name);
                    }

                    foreach (var method in methods)
                    {
                        PatchSpawnMethod(method);
                    }
                }

                PatchRequiredVoid(typeof(BotCreatorClient), "method_3", nameof(BotActivationPrefix));
                PatchRequiredVoid(typeof(BotCreatorClient), "StoreBotRenderers", nameof(StoreRenderersPrefix));
                PatchRequiredVoid(typeof(BotOwner), "PreActivate", nameof(BotPreActivatePrefix));
                PatchBotOwnerCreate();

                // These are the final typed targeting admission points used by the native
                // group and memory code.  CheckAndAddEnemy covers group pursuit rechecks.
                PatchTarget(typeof(BotsGroup), "AddEnemy");
                PatchTarget(typeof(BotsGroup), "CheckAndAddEnemy");
                PatchTarget(typeof(BotMemory), "AddEnemy");
                PatchRequiredVoid(typeof(GameWorld), "RegisterPlayer", nameof(RegisterPlayerPrefix));
                PatchRequiredVoid(typeof(ClientNetworkGameWorld), "RegisterPlayer", nameof(RegisterPlayerPrefix));
                foreach (var worldType in typeof(GameWorld).Assembly.GetTypes())
                {
                    if (
                        !typeof(GameWorld).IsAssignableFrom(worldType)
                        || worldType == typeof(GameWorld)
                        || worldType == typeof(ClientGameWorld)
                        || worldType == typeof(ClientNetworkGameWorld)
                    )
                        continue;
                    if (
                        worldType.GetMethod(
                            "RegisterPlayer",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                        ) != null
                    )
                        throw new NotSupportedException("Unverified player registration override: " + worldType.FullName);
                }
                PatchRequiredVoid(typeof(ClientGameWorld), "RegisterPlayer", nameof(RegisterPlayerPrefix));
                _installed = true;
                _error = "";
            }
            catch (Exception exception)
            {
                _error = "Native encounter admission coverage is unavailable: " + exception.Message;
                _installed = true;
            }

            error = _error;
            return _error.Length == 0;
        }
    }

    internal static bool TryOpen(
        EncounterRuntimeContext context,
        EncounterSpawnAdmission admission,
        EncounterSpawnReservation reservation,
        Vector3 expectedPosition,
        out IDisposable? lease,
        out string error
    )
    {
        lease = null;
        error = "";
        if (!CoverageVerified)
        {
            error = CoverageError.Length == 0 ? "Native encounter admission coverage is not verified." : CoverageError;
            return false;
        }

        if (context == null || admission == null || reservation == null || !Matches(context, reservation))
        {
            error = "The native admission reservation does not match this runtime.";
            return false;
        }

        lock (Gate)
        {
            if (InvalidatedContexts.Contains(ContextKey(context)))
            {
                error = "The native admission runtime generation has been invalidated.";
                return false;
            }
        }

        var current = Current.Value;
        if (current != null && !current.Closed)
        {
            error = "A native bot activation is already admitted on this execution flow.";
            return false;
        }

        var scope = new Scope(context, reservation, expectedPosition);
        Current.Value = scope;
        lock (Gate)
        {
            Scopes[reservation.Token] = scope;
        }
        lease = new Lease(scope);
        return true;
    }

    /// <summary>
    /// Invalidates active execution scopes before the shared reservation registry is cancelled.
    /// This closes the async flow that may be waiting inside SPT's LocalPlayer factory and
    /// disposes players that reached RegisterPlayer before the preview was reset.
    /// </summary>
    internal static void Invalidate(EncounterRuntimeContext context)
    {
        if (context == null)
            return;

        var scopes = new List<Scope>();
        lock (Gate)
        {
            var contextKey = ContextKey(context);
            foreach (var scope in Scopes.Values)
            {
                if (Matches(scope.Context, context))
                    scopes.Add(scope);
            }

            if (scopes.Count == 0)
                InvalidatedContexts.Remove(contextKey);
            else
                InvalidatedContexts.Add(contextKey);
        }

        foreach (var scope in scopes)
        {
            DisposeScopeCleanup(scope.Invalidate());
        }
    }

    internal static bool MarkAdmitted(EncounterRuntimeContext context, EncounterSpawnReservation reservation)
    {
        if (context == null || reservation == null)
            return false;

        lock (Gate)
        {
            if (
                !Scopes.TryGetValue(reservation.Token, out var scope)
                || !Matches(scope.Context, context)
                || !ReferenceEquals(scope.Reservation, reservation)
            )
            {
                return false;
            }

            return scope.MarkAdmitted();
        }
    }

    internal static bool AllowNativeSpawn()
    {
        var current = Current.Value;
        if (current == null || current.Closed || !CoverageVerified)
        {
            return false;
        }

        lock (Gate)
        {
            if (InvalidatedContexts.Contains(ContextKey(current.Context)))
                return false;
        }

        return current.Context.HasIdentity
            && !current.Invalidated
            && current.Reservation.PreviewGeneration == current.Context.PreviewGeneration
            && current.Reservation.SessionId == current.Context.SessionId
            && current.Reservation.RaidId == current.Context.RaidId
            && current.Reservation.LayoutId == current.Context.LayoutId;
    }

    internal static bool IsCurrent(EncounterRuntimeContext context, EncounterSpawnReservation reservation)
    {
        var current = Current.Value;
        return current != null
            && !current.Closed
            && ReferenceEquals(current.Context, context)
            && ReferenceEquals(current.Reservation, reservation)
            && AllowNativeSpawn();
    }

    /// <summary>
    /// A mission has the same strict native admission boundary as editor AI, but it
    /// runs in an ordinary local raid where EditorMode.Active is false. Keep the
    /// context ledger explicit so ambient bot schedulers are suppressed only for
    /// an authenticated mission run and ordinary raids pass through unchanged.
    /// </summary>
    internal static void SetMissionContext(EncounterRuntimeContext context)
    {
        if (context == null || !context.HasIdentity || context.Mode != EncounterRuntimeModes.Mission)
            throw new InvalidOperationException("A server-bound mission encounter context is required.");
        lock (Gate)
        {
            MissionContexts[ContextKey(context)] = context;
        }
    }

    internal static void ClearMissionContext(EncounterRuntimeContext? context)
    {
        if (context == null)
            return;
        lock (Gate)
        {
            MissionContexts.Remove(ContextKey(context));
        }
    }

    private static bool AdmissionActive
    {
        get
        {
            if (EditorMode.Active)
                return true;
            lock (Gate)
            {
                return MissionContexts.Count > 0;
            }
        }
    }

    private static bool Matches(EncounterRuntimeContext context, EncounterSpawnReservation reservation)
    {
        return context.HasIdentity
            && string.Equals(context.SessionId, reservation.SessionId, StringComparison.Ordinal)
            && string.Equals(context.RaidId, reservation.RaidId, StringComparison.Ordinal)
            && string.Equals(context.LayoutId, reservation.LayoutId, StringComparison.Ordinal)
            && context.LayoutRevision == reservation.LayoutRevision
            && string.Equals(context.Mode, reservation.Mode, StringComparison.Ordinal)
            && string.Equals(context.PreviewGeneration, reservation.PreviewGeneration, StringComparison.Ordinal)
            && context.PublishedLayoutConfirmed == reservation.PublishedLayoutConfirmed;
    }

    internal static void SetObserveTarget(Player? player)
    {
        _excludedObservePlayer = player;
    }

    internal static void ClearObserveTarget(Player? player = null)
    {
        if (player == null || ReferenceEquals(_excludedObservePlayer, player))
        {
            _excludedObservePlayer = null;
        }
    }

    internal static bool ShouldExcludeTarget(IPlayer? target)
    {
        var player = _excludedObservePlayer;
        if (player == null || target == null)
        {
            return false;
        }

        if (ReferenceEquals(target, player))
        {
            return true;
        }

        var profileId = player.ProfileId;
        return !string.IsNullOrWhiteSpace(profileId) && string.Equals(target.ProfileId, profileId, StringComparison.Ordinal);
    }

    private static void PatchTarget(Type type, string name)
    {
        var methods = GetMethods(type, name);
        if (methods.Length == 0)
        {
            throw new MissingMethodException(type.FullName, name);
        }

        foreach (var method in methods)
        {
            _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(EncounterSpawnAdmissionGate), nameof(TargetPrefix)));
        }
    }

    private static MethodInfo[] GetMethods(Type type, string name)
    {
        var all = type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly
        );
        var matches = new List<MethodInfo>();
        foreach (var method in all)
        {
            if (method.Name == name && !method.IsAbstract)
                matches.Add(method);
        }

        return matches.ToArray();
    }

    private static void PatchSpawnMethod(MethodInfo method)
    {
        if (method.ReturnType == typeof(void))
        {
            _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(EncounterSpawnAdmissionGate), nameof(NativeSpawnVoidPrefix)));
            return;
        }

        if (method.ReturnType == typeof(Task))
        {
            _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(EncounterSpawnAdmissionGate), nameof(NativeSpawnTaskPrefix)));
            return;
        }

        throw new NotSupportedException("Unsupported native spawn return type: " + method);
    }

    private static void PatchBotOwnerCreate()
    {
        var methods = GetMethods(typeof(BotOwner), "Create");
        if (methods.Length == 0)
            throw new MissingMethodException(typeof(BotOwner).FullName, "Create");

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (
                !method.IsStatic
                || method.ReturnType != typeof(BotOwner)
                || parameters.Length == 0
                || parameters[0].ParameterType != typeof(Player)
            )
                throw new NotSupportedException("Unsupported native BotOwner.Create signature: " + method);

            _harmony!.Patch(
                method,
                prefix: new HarmonyMethod(typeof(EncounterSpawnAdmissionGate), nameof(BotOwnerCreatePrefix)),
                postfix: new HarmonyMethod(typeof(EncounterSpawnAdmissionGate), nameof(BotOwnerCreatePostfix))
            );
        }
    }

    private static void PatchRequiredVoid(Type type, string name, string prefixName)
    {
        var methods = GetMethods(type, name);
        if (methods.Length == 0)
            throw new MissingMethodException(type.FullName, name);

        foreach (var method in methods)
        {
            if (method.ReturnType != typeof(void))
                throw new NotSupportedException("Unsupported native activation return type: " + method);
            _harmony!.Patch(method, prefix: new HarmonyMethod(typeof(EncounterSpawnAdmissionGate), prefixName));
        }
    }

    private static bool NativeSpawnVoidPrefix(object[] __args)
    {
        // The editor's existing restriction patch remains authoritative for ordinary editor
        // gameplay. This prefix adds the reservation requirement to editor and mission runs.
        return !AdmissionActive || ArgumentsMatchCurrentScope(__args);
    }

    private static bool NativeSpawnTaskPrefix(object[] __args, ref Task __result, MethodBase __originalMethod)
    {
        var decision = EncounterSpawnAdmissionPolicy.DecideTask(
            AdmissionActive,
            ArgumentsMatchCurrentScope(__args),
            HasExplicitAdmissionArguments(__args),
            __originalMethod?.DeclaringType?.FullName
        );
        if (decision == EncounterSpawnTaskDecision.PassThrough)
        {
            if (
                AdmissionActive
                && __originalMethod != null
                && __originalMethod.DeclaringType == typeof(BotCreatorClient)
                && string.Equals(__originalMethod.Name, "CreateBot", StringComparison.Ordinal)
                && !TryEnterCreate()
            )
            {
                __result = Task.FromException(
                    new InvalidOperationException("A native profile was already admitted for this encounter reservation.")
                );
                return false;
            }

            return true;
        }

        if (decision == EncounterSpawnTaskDecision.CompleteNoOp)
        {
            // Native wave schedulers still run while the editor map is active.  Their ambient
            // requests have no encounter reservation, so they must complete successfully without
            // entering native spawning; a fault would bubble through WavesSpawnScenario.Run.
            __result = Task.CompletedTask;
            return false;
        }

        // A skipped explicit native method still needs a faulted result so callers cannot mistake
        // an invalid or stale reservation for an empty successful activation.
        __result = Task.FromException(new InvalidOperationException("Native bot spawning is not admitted for this editor preview."));
        return false;
    }

    private static bool BotActivationPrefix(object[] __args)
    {
        if (!AdmissionActive || ArgumentsMatchCurrentScope(__args))
            return true;

        DisposeDeniedBot(FindBot(__args));
        return false;
    }

    private static bool StoreRenderersPrefix(BotCreatorClient __instance, Player __0)
    {
        if (!AdmissionActive)
            return true;
        var scope = Current.Value;
        if (scope == null || !AllowNativeSpawn() || !scope.HasPlayer(__0))
            return false;
        scope.RendererCreator = __instance;
        return true;
    }

    private static bool BotPreActivatePrefix(BotOwner __instance)
    {
        if (!AdmissionActive)
            return true;

        if (__instance != null && MatchesCurrentProfile(__instance.ProfileId) && HasCurrentOwner(__instance))
            return true;

        DisposeDeniedBot(__instance);
        return false;
    }

    private static bool RegisterPlayerPrefix(IPlayer __0)
    {
        if (!AdmissionActive)
            return true;
        if (__0 == null)
            return false;

        // RegisterPlayer is virtual and is also used by the editor's local player.  The
        // scratch identity is established before the local player is registered, so it is the
        // only non-AI identity admitted while editor mode is active.
        if (!__0.IsAI && string.Equals(__0.ProfileId, EditorMode.ScratchProfileId, StringComparison.Ordinal))
            return true;

        // The campaign player is registered by native raid setup outside an
        // encounter reservation. Preserve that registration while still requiring
        // an exact reservation for every AI player.
        if (!__0.IsAI && MissionRaidRuntime.IsMissionPlayer(__0))
            return true;

        if (!MatchesCurrentProfile(__0.ProfileId))
        {
            DisposePlayer(__0 as Player);
            return false;
        }

        var player = __0 as Player;
        if (player == null || !TrackCurrentPlayer(player))
        {
            DisposePlayer(player);
            return false;
        }

        return true;
    }

    private static bool BotOwnerCreatePrefix(Player __0, ref BotOwner __result)
    {
        if (!AdmissionActive)
            return true;

        var current = Current.Value;
        if (
            current != null
            && AllowNativeSpawn()
            && __0 != null
            && MatchesCurrentProfile(__0.ProfileId)
            && current.HasPlayer(__0)
            && current.HasCreateStarted
        )
        {
            if (current.EnterOwnerCreate())
                return true;

            // A duplicate call for the already-bound player must not tear down the valid
            // player that owns the first BotOwner.  It is still rejected before native
            // allocation, so no second owner can enter the world.
            __result = null!;
            return false;
        }

        if (IsScratchPlayer(__0))
        {
            __result = null!;
            return false;
        }

        DisposePlayer(__0);
        __result = null!;
        return false;
    }

    private static void BotOwnerCreatePostfix(Player __0, ref BotOwner __result)
    {
        if (!AdmissionActive || __result == null)
            return;

        var current = Current.Value;
        if (
            current != null
            && AllowNativeSpawn()
            && current.HasPlayer(__0)
            && ReferenceEquals(__result.GetPlayer, __0)
            && current.BindOwner(__result)
        )
            return;

        DisposeDeniedBot(__result);
        __result = null!;
    }

    private static bool TargetPrefix(IPlayer __0)
    {
        return !ShouldExcludeTarget(__0);
    }

    private static bool TryEnterCreate()
    {
        var current = Current.Value;
        return current != null && AllowNativeSpawn() && current.EnterCreate();
    }

    private static bool TrackCurrentPlayer(Player player)
    {
        var current = Current.Value;
        return current != null && AllowNativeSpawn() && current.TrackPlayer(player);
    }

    private static bool HasCurrentOwner(BotOwner owner)
    {
        var current = Current.Value;
        return current != null && current.HasOwner(owner);
    }

    private static BotOwner? FindBot(object[]? arguments)
    {
        if (arguments == null)
            return null;
        foreach (var argument in arguments)
        {
            if (argument is BotOwner owner)
                return owner;
        }

        return null;
    }

    private static bool ArgumentsMatchCurrentScope(object[]? arguments)
    {
        if (!AllowNativeSpawn() || arguments == null)
            return false;

        var profileSeen = false;
        foreach (var argument in arguments)
        {
            if (argument is Profile profile)
            {
                if (!MatchesCurrentProfile(profile.Id))
                    return false;
                profileSeen = true;
                continue;
            }

            if (argument is BotOwner owner)
            {
                if (!MatchesCurrentProfile(owner.ProfileId))
                    return false;
                profileSeen = true;
                continue;
            }

            if (argument is BotCreationData data)
            {
                if (data.Profiles == null || data.Profiles.Count != 1)
                    return false;
                var dataProfile = data.Profiles[0];
                if (dataProfile == null || !MatchesCurrentProfile(dataProfile.Id))
                    return false;
                if (data._positions == null || data._positions.Count != 1)
                    return false;
                if (!PositionMatchesCurrentScope(data._positions[0]))
                    return false;
                profileSeen = true;
                continue;
            }

            if (argument is PositionNote position && !PositionMatchesCurrentScope(position))
                return false;
        }

        return profileSeen;
    }

    private static bool HasExplicitAdmissionArguments(object[]? arguments)
    {
        if (arguments == null)
            return false;

        foreach (var argument in arguments)
        {
            if (argument is Profile or BotOwner or BotCreationData or PositionNote)
                return true;
        }

        return false;
    }

    private static bool PositionMatchesCurrentScope(PositionNote? position)
    {
        var current = Current.Value;
        if (current == null || position == null)
            return false;

        return (position.position - current.ExpectedPosition).sqrMagnitude <= 0.0001f;
    }

    private static bool MatchesCurrentProfile(string? profileId)
    {
        var current = Current.Value;
        return current != null
            && !current.Closed
            && !string.IsNullOrWhiteSpace(profileId)
            && string.Equals(profileId, current.Reservation.ProfileId, StringComparison.Ordinal)
            && AllowNativeSpawn();
    }

    private static bool Matches(EncounterRuntimeContext left, EncounterRuntimeContext right)
    {
        return left != null && right != null && left.Matches(right);
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
            context.PublishedLayoutConfirmed ? "1" : "0"
        );
    }

    private static void DisposeDeniedBot(BotOwner? bot)
    {
        if (bot == null)
            return;

        try
        {
            if (bot.BotState == EBotState.PreActive)
                bot.BotState = EBotState.ActiveFail;
            bot.Dispose();
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT denied encounter bot cleanup failed: " + exception);
        }

        DisposePlayer(bot.GetPlayer);
    }

    private static void DisposeScopeCleanup(ScopeCleanup cleanup)
    {
        if (cleanup.RendererCreator != null)
            foreach (var player in cleanup.Players)
                cleanup.RendererCreator._botRenders.Remove(player);
        if (cleanup == null)
            return;

        var ownerPlayer = cleanup.Owner?.GetPlayer;
        if (cleanup.Owner != null)
            DisposeDeniedBot(cleanup.Owner);

        foreach (var player in cleanup.Players)
        {
            if (ownerPlayer != null && ReferenceEquals(ownerPlayer, player))
                continue;
            DisposePlayer(player);
        }
    }

    private static void DisposePlayer(Player? player)
    {
        if (player == null || IsScratchPlayer(player))
            return;

        try
        {
            EncounterPlayerCleanup.Dispose(player);
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogError("WTT denied encounter player cleanup failed: " + exception);
        }
    }

    private static bool IsScratchPlayer(Player? player)
    {
        return player != null && !player.IsAI && string.Equals(player.ProfileId, EditorMode.ScratchProfileId, StringComparison.Ordinal);
    }
}
