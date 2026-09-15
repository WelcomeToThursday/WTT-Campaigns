using System.Collections;
using System.Reflection;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.Jobs;
using EFT;
using UnityEngine;
using UnityEngine.SceneManagement;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>
/// Owns one normal mission raid.  The server descriptor is the only source of
/// layout content; ordinary raids never enter this runtime.
/// </summary>
internal sealed class MissionRaidRuntime : MonoBehaviour
{
    private sealed class Pending
    {
        internal MissionDescriptor Descriptor = null!;
        internal MissionRun Run = null!;
        internal long Revision;
    }

    private sealed class MissionRouteTrigger : MonoBehaviour, IPhysicsTriggerWithStay
    {
        private MissionRaidRuntime? _owner;
        private string _kind = "";
        private string _id = "";

        internal void Initialize(MissionRaidRuntime owner, string kind, string id)
        {
            _owner = owner;
            _kind = kind;
            _id = id;
        }

        public string Description => "Authored mission route";

        public void OnTriggerEnter(Collider other) => _owner?.RouteEntered(_kind, _id, other);

        public void OnTriggerExit(Collider other) { }

        void IPhysicsTriggerWithStay.OnTriggerStay(Collider other, Collider trigger) => OnTriggerEnter(other);

        private void OnTriggerStay(Collider other) => OnTriggerEnter(other);

        private void OnDestroy() => _owner = null;
    }

    private static Pending? _pending;
    internal static MissionRaidRuntime Instance = null!;

    private readonly List<GameObject> _routeVolumes = new();
    private readonly List<(GameObject Object, bool Active)> _disabledExtracts = new();
    private readonly Dictionary<string, string> _progressOperations = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _progressGate = new(1, 1);
    private MissionDescriptor? _descriptor;
    private MissionRun? _run;
    private MissionHud? _hud;
    private MissionLoot? _loot;
    private MapSceneAdapter? _scene;
    private EncounterPreviewRuntime? _encounters;
    private EncounterRuntimeContext? _missionContext;
    private CancellationTokenSource? _lifetime;
    private Player? _player;
    private GameWorld? _startupWorld;
    private long _revision;
    private int _runtimeGeneration;
    private bool _starting;
    private bool _active;
    private bool _ending;
    private bool _successfulExitRequested;

    internal static bool Active => Instance && Instance._active;
    internal static bool Starting => Instance && Instance._starting;
    internal static bool HasPending => _pending != null;

    /// <summary>Run marker for the native start request; never exposes another character's run.</summary>
    internal static string PendingRunId
    {
        get
        {
            var pending = _pending;
            var character = MissionClient.CharacterId;
            return
                pending?.Run != null
                && !string.IsNullOrWhiteSpace(character)
                && string.Equals(pending.Run.CharacterId, character, StringComparison.Ordinal)
                ? pending.Run.RunId
                : "";
        }
    }

    internal static bool IsMissionPlayer(IPlayer? player)
    {
        if (player == null || !Starting)
            return false;
        var profileId = MissionClient.CharacterId;
        return !string.IsNullOrWhiteSpace(profileId) && string.Equals(player.ProfileId, profileId, StringComparison.Ordinal);
    }

    internal static void SetPending(MissionDescriptor descriptor, MissionRun run, long revision)
    {
        if (!Instance)
            throw new InvalidOperationException("The mission runtime is not initialized.");
        if (descriptor == null || run == null || string.IsNullOrWhiteSpace(run.RunId))
            throw new InvalidOperationException("The mission server returned an incomplete run descriptor.");
        _pending = new Pending
        {
            Descriptor = descriptor,
            Run = run,
            Revision = revision,
        };
    }

    internal static void ClearPending() => _pending = null;

    internal static void AbortPendingRaid(string reason)
    {
        ClearPending();
        if (!Plugin.InRaid || Plugin.Player == null || Singleton<AbstractGame>.Instance is not LocalGame game)
            return;
        try
        {
            // This path is only used when local mission launch itself failed
            // before the authored runtime became active. Preserve native gear
            // while the server records the cancelled/failed prepared run.
            game.Stop(Plugin.Player.Profile.Id, ExitStatus.Survived, reason, 0);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }

    private void Awake()
    {
        Instance = this;
        MissionStartupGuard.Install();
    }

    private void Update()
    {
        if (!Plugin.InRaid || !Plugin.SeasonalPlayer)
        {
            if (_active || _starting)
                EndRuntime();
            return;
        }

        var player = Plugin.Player;
        if (player == null)
        {
            if (_active || _starting)
                EndRuntime();
            return;
        }
        if (!_active && !_starting && _pending != null)
        {
            _starting = true;
            _ = StartPendingAsync(_pending);
            return;
        }
        if (_starting)
        {
            MissionStartupGuard.Hold(player);
            if (
                _startupWorld != null
                && (!Singleton<GameWorld>.Instantiated || !ReferenceEquals(_startupWorld, Singleton<GameWorld>.Instance))
            )
                EndRuntime();
            return;
        }
        if (!_active)
            return;
        if (_player != player)
        {
            EndRuntime();
            return;
        }
        if (_startupWorld != null && (!Singleton<GameWorld>.Instantiated || !ReferenceEquals(_startupWorld, Singleton<GameWorld>.Instance)))
        {
            EndRuntime();
            return;
        }

        if (!_ending && player.HealthController?.IsAlive == false)
            _hud?.SetStatus("Operator down · the mission attempt has failed");
        try
        {
            _encounters?.Tick();
            if (!_ending && !string.IsNullOrWhiteSpace(_encounters?.Failure))
            {
                _hud?.SetStatus("Encounter failed · mission attempt ended");
                RequestNativeFailure("Mission encounter failed");
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            _hud?.SetStatus("Encounter runtime stopped: " + exception.Message);
            RequestNativeFailure("Mission encounter runtime stopped");
        }
    }

    private async Task StartPendingAsync(Pending pending)
    {
        var lifetime = _lifetime = new CancellationTokenSource();
        var runtimeGeneration = ++_runtimeGeneration;
        MissionResponse? latestResponse = null;
        try
        {
            if (!Plugin.InRaid || Plugin.Player == null)
                throw new InvalidOperationException("The mission raid player is unavailable.");
            _player = Plugin.Player;
            _startupWorld = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (_startupWorld == null)
                throw new InvalidOperationException("The mission world is unavailable.");
            MissionStartupGuard.Begin(_player);
            // Disable ordinary extracts before the first awaited descriptor request.
            // The startup guard holds the player in place until all authored content is ready.
            DisableOrdinaryExtracts();

            // StartLocalRaid assigns the native raid ID asynchronously. Resolve the
            // descriptor after that hook commits so encounter admission is bound to it.
            MissionResponse? response = null;
            Exception? last = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    response = await MissionClient.DescriptorAsync(
                        pending.Run.MissionId,
                        pending.Run.RunId,
                        pending.Run.RaidId,
                        lifetime.Token
                    );
                    latestResponse = response;
                    if (response.Run != null)
                    {
                        // StartLocalRaid binds the native ID after prepare. Keep
                        // that authoritative identity for any startup cleanup.
                        pending.Run = response.Run;
                        pending.Revision = response.Revision;
                        _revision = response.Revision;
                    }
                    if (response.Descriptor != null && response.Run?.Status == MissionRunStatuses.Active)
                        break;
                    last = new InvalidOperationException(
                        response.Message.Length > 0 ? response.Message : "Mission raid is not active yet."
                    );
                }
                catch (Exception exception)
                {
                    last = exception;
                }
                await UniTask.Delay(100, delayType: DelayType.Realtime, cancellationToken: lifetime.Token);
            }

            EnsureStartupWorld();

            var descriptor =
                response?.Descriptor ?? throw (last ?? new InvalidOperationException("The mission descriptor is unavailable."));
            var run = response.Run ?? throw new InvalidOperationException("The mission run state is unavailable.");
            ValidateDescriptor(descriptor, run);
            _descriptor = descriptor;
            _run = run;
            _revision = response.Revision;
            _missionContext = new EncounterRuntimeContext
            {
                SessionId = descriptor.SessionId,
                RaidId = descriptor.RaidId,
                LayoutId = descriptor.Layout.Id,
                LayoutRevision = descriptor.ContentRevision,
                Mode = EncounterRuntimeModes.Mission,
                PreviewGeneration = run.RunId,
                PublishedLayoutConfirmed = descriptor.EncounterToken.Length > 0,
            };
            // Native raid setup runs outside an encounter reservation. Register the
            // authenticated mission context before any scene, loot or encounter
            // work so ambient bot schedulers are suppressed for this raid while
            // ordinary raids remain untouched.
            EncounterSpawnAdmissionGate.SetMissionContext(_missionContext);
            _scene = new MapSceneAdapter();
            _scene.ApplyMission(descriptor.Layout);
            await MapSceneAdapter.WaitForNavigationAsync(lifetime.Token);
            EnsureStartupWorld();
            _loot = new MissionLoot();
            await _loot.ApplyAsync(descriptor.Layout, run.RunId, lifetime.Token);
            EnsureStartupWorld();
            ZoneRuntime.Instance?.BeginMission(descriptor.Zones.AsValueEnumerable().Where(z => !ZoneLayoutRules.IsShared(z)).ToArray());
            CreateRouteVolumes(descriptor.Layout);
            if (descriptor.Layout.Start == null)
                throw new InvalidOperationException("The mission layout has no authored start.");
            var startPosition = ZoneRuntime.Vector(descriptor.Layout.Start.Position);
            if (!ClearPlayerPosition(startPosition, _player!, descriptor.Layout))
                throw new InvalidOperationException("The authored mission start does not have clear standing space and a floor.");

            _encounters = new EncounterPreviewRuntime();
            await _encounters.BeginAsync(
                _missionContext!,
                descriptor.Layout,
                _player,
                observe: false,
                lifetime.Token,
                descriptor.EncounterToken
            );
            EnsureStartupWorld();
            // Release the startup hold only after native readiness is complete,
            // then place the player at the authored start in the same frame.
            _player!.Teleport(startPosition);
            _player.Rotation = new Vector2(descriptor.Layout.Start.Rotation.Y, descriptor.Layout.Start.Rotation.X);
            _hud = new MissionHud(descriptor.Definition.Name);
            _active = true;
            _ending = false;
            _successfulExitRequested = false;
            _hud.SetRoute(run.NextCheckpointIndex, descriptor.Layout.Checkpoints.Count, run.ExitReached, "Mission active");
            _encounters.MissionStart();
            _pending = null;
            MissionStartupGuard.End(_player);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            if (latestResponse?.Run != null)
                pending.Run = latestResponse.Run;
            await CancelFailedStart(pending, "Mission launch cancelled.");
            if (IsStartupWorldCurrent())
                RequestNativeStartupFailure("Mission launch cancelled");
            EndRuntime();
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            if (latestResponse?.Run != null)
                pending.Run = latestResponse.Run;
            _hud?.SetStatus("Mission unavailable: " + exception.Message);
            await CancelFailedStart(pending, exception.Message);
            if (IsStartupWorldCurrent())
                RequestNativeStartupFailure("Mission startup failed");
            EndRuntime();
        }
        finally
        {
            _starting = false;
            if (_lifetime == lifetime && !_active)
            {
                _lifetime.Dispose();
                _lifetime = null;
            }
        }
    }

    private bool IsStartupWorldCurrent()
    {
        return Plugin.InRaid
            && Plugin.Player != null
            && _player != null
            && ReferenceEquals(Plugin.Player, _player)
            && _startupWorld != null
            && Singleton<GameWorld>.Instantiated
            && ReferenceEquals(_startupWorld, Singleton<GameWorld>.Instance);
    }

    private void EnsureStartupWorld()
    {
        if (!IsStartupWorldCurrent())
            throw new OperationCanceledException("The mission raid ended while its authored world was loading.");
    }

    private async Task CancelFailedStart(Pending pending, string reason)
    {
        try
        {
            if (pending.Run.Status is MissionRunStatuses.Prepared or MissionRunStatuses.Active)
            {
                var response = await MissionClient.CancelAsync(
                    pending.Run.MissionId,
                    pending.Run.RunId,
                    pending.Run.RaidId,
                    _revision == 0 ? pending.Revision : _revision
                );
                if (!string.IsNullOrWhiteSpace(response.Error))
                    Plugin.LogInfo("Mission launch cancellation was rejected: " + response.Error);
            }
        }
        catch (Exception exception)
        {
            Plugin.Error(new InvalidOperationException("Mission launch cleanup failed: " + reason, exception));
        }
        finally
        {
            _pending = null;
        }
    }

    private void ValidateDescriptor(MissionDescriptor descriptor, MissionRun run)
    {
        var character = MissionClient.CharacterId;
        if (descriptor.CharacterId != character || run.CharacterId != character)
            throw new InvalidOperationException("The mission descriptor belongs to another campaign character.");
        if (descriptor.RunId != run.RunId || run.MissionId != descriptor.Definition.Id)
            throw new InvalidOperationException("The mission descriptor and run identities do not match.");
        if (string.IsNullOrWhiteSpace(descriptor.RaidId) || descriptor.RaidId != run.RaidId)
            throw new InvalidOperationException("The mission server did not bind the run to this raid.");
        if (descriptor.Layout.Id != descriptor.Definition.LayoutId || run.LayoutId != descriptor.Layout.Id)
            throw new InvalidOperationException("The mission descriptor layout identity is invalid.");
        if (descriptor.ContentRevision != run.ContentRevision || descriptor.ContentHash != run.ContentHash)
            throw new InvalidOperationException("The mission descriptor content revision is stale.");
        if (!string.Equals(descriptor.Layout.Location, ZoneRuntime.Location, StringComparison.Ordinal))
            throw new InvalidOperationException("The mission descriptor map does not match the active raid.");
        if (descriptor.Layout.Start == null || descriptor.Layout.Exit == null || descriptor.Layout.Checkpoints.Count == 0)
            throw new InvalidOperationException("The mission layout requires an authored start, route and exit.");
        if (descriptor.EncounterToken.Length == 0)
            throw new InvalidOperationException("The mission descriptor has no encounter authorization token.");
    }

    private void CreateRouteVolumes(MapLayout layout)
    {
        foreach (var checkpoint in layout.Checkpoints)
            CreateRouteVolume(checkpoint, "Checkpoint");
        if (layout.Exit != null)
            CreateRouteVolume(layout.Exit, "Exit");
    }

    private void CreateRouteVolume(MapVolume volume, string kind)
    {
        if (string.IsNullOrWhiteSpace(volume.Scene))
            throw new InvalidOperationException("The authored " + kind.ToLowerInvariant() + " has no scene.");
        var scene = SceneManager.GetSceneByName(volume.Scene);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("The authored " + kind.ToLowerInvariant() + " scene is unavailable: " + volume.Scene);
        var root = new GameObject("Mission " + kind + " " + volume.Name);
        root.layer = LayerMask.NameToLayer("Triggers");
        root.transform.SetPositionAndRotation(ZoneRuntime.Vector(volume.Position), Quaternion.Euler(ZoneRuntime.Vector(volume.Rotation)));
        if (volume.Shape == "Sphere")
        {
            var collider = root.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = volume.Radius;
        }
        else if (volume.Shape == "Box")
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = ZoneRuntime.Vector(volume.Size);
        }
        else
        {
            Destroy(root);
            throw new InvalidOperationException("The authored " + kind.ToLowerInvariant() + " shape is unsupported.");
        }
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<MissionRouteTrigger>().Initialize(this, kind, volume.Id);
        _routeVolumes.Add(root);
    }

    private static bool ClearPlayerPosition(Vector3 position, Player player, MapLayout layout)
    {
        var navigation = new EncounterNavigation(() => layout);
        var authored = new SpatialVector
        {
            X = position.x,
            Y = position.y,
            Z = position.z,
        };
        if (!navigation.HasStandingClearance(authored, player))
            return false;
        var mask = Physics.DefaultRaycastLayers;
        var floor = Physics
            .RaycastAll(position + Vector3.up * .15f, Vector3.down, 1.5f, mask, QueryTriggerInteraction.Ignore)
            .AsValueEnumerable()
            .Where(hit => hit.collider && hit.collider.GetComponentInParent<Player>() == null)
            .OrderBy(hit => hit.distance)
            .FirstOrDefault();
        return floor.collider && floor.normal.y >= .5f;
    }

    private void DisableOrdinaryExtracts()
    {
        var seen = new HashSet<GameObject>();
        var world = Singleton<GameWorld>.Instance;
        var controllerProperty = typeof(GameWorld).GetProperty(
            "ExfiltrationController",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        );
        var controller = controllerProperty?.GetValue(world);
        if (controller == null)
            throw new InvalidOperationException("The native extraction controller is unavailable for a mission raid.");
        foreach (var memberName in new[] { "ExfiltrationPoints", "ScavExfiltrationPoints", "SecretExfiltrationPoints" })
        {
            var member =
                controller
                    .GetType()
                    .GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(controller)
                ?? controller
                    .GetType()
                    .GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(controller);
            if (member is not IEnumerable points)
                continue;
            foreach (var point in points)
            {
                var gameObject = point switch
                {
                    GameObject value => value,
                    Component value => value.gameObject,
                    _ => null,
                };
                if (!gameObject || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded || !seen.Add(gameObject))
                    continue;
                _disabledExtracts.Add((gameObject, gameObject.activeSelf));
                gameObject.SetActive(false);
            }
        }
    }

    private void RestoreOrdinaryExtracts()
    {
        foreach (var (gameObject, active) in _disabledExtracts)
            if (gameObject)
                gameObject.SetActive(active);
        _disabledExtracts.Clear();
    }

    private void RouteEntered(string kind, string id, Collider other)
    {
        if (!_active || _ending || _player == null || !Singleton<GameWorld>.Instantiated)
            return;
        var player = Singleton<GameWorld>.Instance.GetPlayerByCollider(other);
        if (!player || player != _player)
            return;
        if (kind == "Checkpoint")
        {
            var index = _descriptor!.Layout.Checkpoints.FindIndex(checkpoint => checkpoint.Id == id);
            if (index != _run!.NextCheckpointIndex)
                return;
            _ = ReportProgressAsync(id, "Checkpoint", index, ProgressOperation("Checkpoint", id));
        }
        else if (kind == "Exit" && _run!.NextCheckpointIndex == _descriptor!.Layout.Checkpoints.Count)
        {
            _ = ReportProgressAsync(id, "Exit", -1, ProgressOperation("Exit", id));
        }
    }

    private string ProgressOperation(string kind, string id)
    {
        var key = kind + "|" + id;
        if (_progressOperations.TryGetValue(key, out var operation))
            return operation;
        operation = MissionClient.NewOperationId();
        _progressOperations[key] = operation;
        return operation;
    }

    private async Task ReportProgressAsync(string id, string kind, int checkpointIndex, string operationId)
    {
        var lifetime = _lifetime;
        var player = _player;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var run = _run;
        var descriptor = _descriptor;
        var generation = _runtimeGeneration;
        if (lifetime == null || player == null || world == null || run == null || descriptor == null)
            return;

        var acquired = false;
        try
        {
            await _progressGate.WaitAsync(lifetime.Token);
            acquired = true;
            if (!IsProgressCurrent(lifetime, player, world, run, descriptor, generation))
                return;
            if (kind == "Checkpoint" && checkpointIndex != _run!.NextCheckpointIndex)
                return;
            if (kind == "Exit" && _run!.NextCheckpointIndex != _descriptor!.Layout.Checkpoints.Count)
                return;
            var response = await MissionClient.ProgressAsync(
                run.MissionId,
                run.RunId,
                run.RaidId,
                id,
                kind,
                _revision,
                operationId: operationId,
                cancellationToken: lifetime.Token
            );
            if (!IsProgressCurrent(lifetime, player, world, run, descriptor, generation))
                return;
            if (
                response.Run != null
                && (
                    !string.Equals(response.Run.RunId, run.RunId, StringComparison.Ordinal)
                    || !string.Equals(response.Run.RaidId, run.RaidId, StringComparison.Ordinal)
                    || !string.Equals(response.Run.MissionId, run.MissionId, StringComparison.Ordinal)
                )
            )
                return;
            _revision = response.Revision;
            if (response.Run != null)
                _run = response.Run;
            if (_run == null)
                return;
            if (kind == "Checkpoint")
            {
                _run.NextCheckpointIndex = Math.Max(_run.NextCheckpointIndex, checkpointIndex + 1);
                _hud?.SetRoute(_run.NextCheckpointIndex, descriptor.Layout.Checkpoints.Count, _run.ExitReached, "Checkpoint secured");
            }
            else
            {
                _run.ExitReached = true;
                _hud?.SetRoute(_run.NextCheckpointIndex, descriptor.Layout.Checkpoints.Count, true, "Extracting…");
                if (response.Committed)
                    RequestNativeExit();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Raid teardown cancels queued and in-flight progress. The cancelled
            // callback must not update a replacement run or surface a false error.
        }
        catch (Exception exception)
        {
            if (!IsProgressCurrent(lifetime, player, world, run, descriptor, generation))
                return;
            Plugin.Error(exception);
            _hud?.SetStatus("Mission progress failed: " + exception.Message);
        }
        finally
        {
            if (acquired)
                _progressGate.Release();
        }
    }

    private bool IsProgressCurrent(
        CancellationTokenSource lifetime,
        Player player,
        GameWorld world,
        MissionRun run,
        MissionDescriptor descriptor,
        int generation
    )
    {
        return MissionProgressCallbackGuard.IsCurrent(
            active: _active,
            ending: _ending,
            lifetimeCancelled: lifetime.IsCancellationRequested,
            sameLifetime: ReferenceEquals(_lifetime, lifetime),
            inRaid: Plugin.InRaid,
            samePlayer: ReferenceEquals(_player, player) && ReferenceEquals(Plugin.Player, player),
            sameWorld: Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, world),
            sameDescriptor: ReferenceEquals(_descriptor, descriptor),
            currentGeneration: _runtimeGeneration,
            capturedGeneration: generation,
            currentRunId: _run?.RunId,
            capturedRunId: run.RunId,
            currentRaidId: _run?.RaidId,
            capturedRaidId: run.RaidId,
            currentMissionId: _run?.MissionId,
            capturedMissionId: run.MissionId
        );
    }

    private void RequestNativeExit()
    {
        if (_successfulExitRequested || _player == null)
            return;
        _successfulExitRequested = true;
        _ending = true;
        try
        {
            if (Singleton<AbstractGame>.Instance is not LocalGame game)
                throw new InvalidOperationException("The local raid lifecycle is unavailable.");
            game.Stop(_player.Profile.Id, ExitStatus.Survived, _descriptor?.Layout.Exit?.Name ?? "Mission exit", 0);
        }
        catch (Exception exception)
        {
            _successfulExitRequested = false;
            _ending = false;
            Plugin.Error(exception);
            _hud?.SetStatus("The authored exit could not end the raid: " + exception.Message);
        }
    }

    private void RequestNativeFailure(string reason)
    {
        if (_ending || _player == null)
            return;
        _ending = true;
        try
        {
            if (Singleton<AbstractGame>.Instance is not LocalGame game)
                throw new InvalidOperationException("The local raid lifecycle is unavailable.");
            game.Stop(_player.Profile.Id, ExitStatus.Killed, reason, 0);
        }
        catch (Exception exception)
        {
            _ending = false;
            Plugin.Error(exception);
            _hud?.SetStatus("The failed mission raid could not close: " + exception.Message);
        }
    }

    private void RequestNativeStartupFailure(string reason)
    {
        if (_ending || _player == null)
            return;
        _ending = true;
        try
        {
            if (Singleton<AbstractGame>.Instance is not LocalGame game)
                throw new InvalidOperationException("The local raid lifecycle is unavailable.");
            // Startup protection is a preflight failure. Preserve native gear and
            // inventory reconciliation while the server records a failed attempt.
            game.Stop(_player.Profile.Id, ExitStatus.Survived, reason, 0);
        }
        catch (Exception exception)
        {
            _ending = false;
            Plugin.Error(exception);
            _hud?.SetStatus("The failed mission raid could not close: " + exception.Message);
        }
    }

    private void EndRuntime()
    {
        _active = false;
        _ending = true;
        _lifetime?.Cancel();
        try
        {
            _encounters?.Reset();
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        _encounters = null;
        EncounterSpawnAdmissionGate.ClearMissionContext(_missionContext);
        _missionContext = null;
        try
        {
            _scene?.Dispose();
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        _scene = null;
        try
        {
            _loot?.Dispose();
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
        _loot = null;
        ZoneRuntime.Instance?.EndMission();
        RestoreOrdinaryExtracts();
        foreach (var volume in _routeVolumes)
            if (volume)
                Destroy(volume);
        _routeVolumes.Clear();
        _hud?.Dispose();
        _hud = null;
        _descriptor = null;
        _run = null;
        _player = null;
        _startupWorld = null;
        _progressOperations.Clear();
        _revision = 0;
        _successfulExitRequested = false;
        if (_lifetime != null)
        {
            if (!_starting)
            {
                _lifetime.Dispose();
                _lifetime = null;
            }
        }
        MissionStartupGuard.End();
    }

    private void OnDestroy()
    {
        EndRuntime();
        if (Instance == this)
            Instance = null!;
    }
}
