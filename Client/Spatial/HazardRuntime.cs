using System.Collections;
using System.Threading;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.CameraControl;
using EFT.EnvironmentEffect;
using EFT.Interactive;
using Systems.Effects;
using UnityEngine;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Authoring.Editor;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Spatial;

// Owns only authored hazards. Native map hazards and their registries are never reset here.
public sealed class HazardRuntime : MonoBehaviour
{
    private readonly Dictionary<Player, Coroutine?> _occupants = new();
    private readonly List<Player> _leaving = new();
    private readonly List<Player> _players = new();
    private readonly Collider[] _contacts = new Collider[128];
    private readonly HashSet<Player> _touching = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, Coroutine> _shotSounds = new();
    private HazardAssets? _assets;
    private bool _ready;
    private SeasonZone? _zone;
    private BorderZone? _border;
    private CampaignBarbedWire? _wire;
    private MineDirectional? _mine;
    private Material? _material;
    private bool _spent;
    private bool _cleared;
    private float _nextTick;

    internal static void Attach(GameObject root, SeasonZone zone)
    {
        if (zone.Hazard == null || EditorMode.Active)
            return;
        _ = root.AddComponent<HazardRuntime>().AttachAsync(zone);
    }

    private async Task AttachAsync(SeasonZone zone)
    {
        try
        {
            await Initialize(zone, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Clear();
            Plugin.Error(error);
        }
    }

    internal async Task Initialize(SeasonZone zone, CancellationToken token)
    {
        var errors = HazardRules.Errors(zone).AsValueEnumerable().ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException(string.Join("\n", errors));
        _zone = zone;
        var box = GetComponent<BoxCollider>();
        box.enabled = false;
        using var loading = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        _assets = await HazardAssets.Load(zone.Hazard!.Kind, zone.Hazard.PlayShotSound, zone.Hazard.SuppressedShots, loading.Token);
        if (_cleared || loading.IsCancellationRequested)
        {
            _assets.Dispose();
            _assets = null;
            loading.Token.ThrowIfCancellationRequested();
            return;
        }
        // Occupancy is sampled once per player, avoiding EFT's multiple body colliders
        // starting duplicate native border coroutines. Physics cannot invoke the native children.
        var child = new GameObject("Native hazard");
        child.transform.SetParent(transform, false);
        switch (zone.Hazard!.Kind)
        {
            case "Minefield":
                var field = child.AddComponent<CampaignMinefield>();
                field._zoneDistortionPower = 0;
                field._explosionTarget = BodyTargets();
                field._collateralDamageTarget = BodyTargets();
                field.PlayerShotEvent += MineEffect;
                _border = field;
                break;
            case "Sniper":
                var sniper = child.AddComponent<CampaignSniperZone>();
                sniper._triggerZoneTarget = BodyTargets();
                sniper._bufferZoneTarget = BodyTargets();
                sniper._firstShotHitProbability = 0;
                sniper.PlayerShotEvent += SniperEffect;
                _border = sniper;
                break;
            case "Claymore":
                if (!Singleton<ItemFactory>.Instance.ItemTemplates.ContainsKey("5996f6cb86f774678763a6ca"))
                    throw new InvalidOperationException("Native mine fragment ammunition is unavailable.");
                _mine = child.AddComponent<MineDirectional>();
                _mine.enabled = false;
                _mine._mineData = new MineDirectional.MineSettings
                {
                    _minExplosionDistance = 1,
                    _maxExplosionDistance = 10,
                    _strength = 150,
                    _armorDamage = .5f,
                    _penetrationPower = 30,
                    _staminaBurnRate = 5,
                    _contusion = new Vector3(10, 1, 10),
                    _armorDistanceDistanceDamage = new Vector3(1, 10, 150),
                    _directionalDamageAngle = 60,
                    _directionalDamageMultiplier = 2,
                    _fragmentType = "5996f6cb86f774678763a6ca",
                    _tag = "Campaign hazard " + zone.Id,
                    _fxName = "Grenade_new",
                };
                // Origin is at the rear of the trigger volume, facing local +Z.
                child.transform.localPosition = new Vector3(0, -.5f * zone.Size.Y + .2f, -.5f * zone.Size.Z);
                BuildClaymore(child.transform);
                break;
            case "BarbedWire":
                var wireBox = child.AddComponent<BoxCollider>();
                wireBox.size = ZoneRuntime.Vector(zone.Size);
                wireBox.isTrigger = true;
                wireBox.enabled = false;
                _wire = child.AddComponent<CampaignBarbedWire>();
                _wire.enabled = false;
                _wire._soundBank = _assets.WireSound;
                BuildWire(zone.Size);
                break;
        }
        if (_border)
        {
            _border!.enabled = false;
            _border.Collider = box;
            _border._extents = box.size * .5f;
            _border._triggerZoneSettings = Vector4.zero;
        }
        _ready = true;
    }

    private static List<EBodyPart> BodyTargets() =>
        new() { EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg };

    private void FixedUpdate()
    {
        if (!_ready || _zone == null || _spent || Time.time < _nextTick)
            return;
        _nextTick = Time.time + .1f;
        if (!Singleton<GameWorld>.Instantiated || RaidEditor.HazardsSuppressed)
        {
            ClearOccupants();
            return;
        }
        var world = Singleton<GameWorld>.Instance;
        _touching.Clear();
        if (_wire)
        {
            var count = Physics.OverlapBoxNonAlloc(
                transform.position,
                ZoneRuntime.Vector(_zone.Size) * .5f,
                _contacts,
                transform.rotation,
                LayersMaskController.HitColliderMask,
                QueryTriggerInteraction.Ignore
            );
            for (var i = 0; i < count; i++)
            {
                var body = _contacts[i].GetComponent<BodyPartCollider>();
                var profileId = body?.playerBridge?.iPlayer?.ProfileId;
                var bridge = profileId == null ? null : world.GetAlivePlayerBridgeByProfileID(profileId);
                if (bridge?.iPlayer?.HealthController?.IsAlive != true)
                    continue;
                var player = world.GetAlivePlayerByProfileID(bridge.iPlayer.ProfileId);
                if (!player)
                    continue;
                _touching.Add(player);
                if (!_occupants.ContainsKey(player))
                {
                    _occupants.Add(player, null);
                    CampaignWireOccupancy.Enter(player, _wire!, bridge);
                }
                _wire!.ProceedDamage(bridge, body!);
            }
        }
        else
        {
            // A native mine explosion can synchronously remove several living players.
            // Keep iteration stable without allocating a fresh array every physics tick.
            _players.Clear();
            _players.AddRange(world.AllAlivePlayersList);
            foreach (var player in _players)
            {
                if (!player || player.HealthController?.IsAlive != true || !Contains(player.Position + Vector3.up * .5f))
                    continue;
                _touching.Add(player);
                if (_occupants.ContainsKey(player))
                    continue;
                if (_mine)
                {
                    _spent = true;
                    // Use a fresh raid calculator; the native static lazy calculator can retain a previous raid.
                    _mine!.Explosion(_mine._mineData, _mine.transform.position, world.SharedBallisticsCalculator);
                    MineEffect(world.GetAlivePlayerBridgeByProfileID(player.ProfileId), null!, 0, true);
                    _mine.gameObject.SetActive(false);
                    break;
                }
                if (_border is CampaignMinefield field)
                    field.Add(player);
                else if (_border is CampaignSniperZone sniper)
                    sniper.Add(player);
                // Run native damage scheduling on this owner so removal cancels pending shots immediately.
                _occupants.Add(player, StartCoroutine(_border!.FireCoroutine(player)));
            }
        }
        _leaving.Clear();
        foreach (var player in _occupants.Keys)
            if (!player || !_touching.Contains(player))
                _leaving.Add(player);
        foreach (var player in _leaving)
            Leave(player);
    }

    private bool Contains(Vector3 position)
    {
        var p = transform.InverseTransformPoint(position);
        var half = ZoneRuntime.Vector(_zone!.Size) * .5f;
        return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
    }

    private void Leave(Player player)
    {
        if (!_occupants.TryGetValue(player, out var routine))
            return;
        if (_shotSounds.TryGetValue(player.ProfileId, out var sound))
        {
            StopCoroutine(sound);
            _shotSounds.Remove(player.ProfileId);
        }
        if (routine != null)
            StopCoroutine(routine);
        if (_border is CampaignMinefield field)
            field.Remove(player);
        else if (_border is CampaignSniperZone sniper)
            sniper.Remove(player);
        if (_wire)
            CampaignWireOccupancy.Leave(player);
        _occupants.Remove(player);
    }

    private void ClearOccupants()
    {
        foreach (var player in _occupants.Keys.AsValueEnumerable().ToArray())
            Leave(player);
    }

    internal void Clear()
    {
        if (_cleared)
            return;
        _cleared = true;
        _lifetime.Cancel();
        enabled = false;
        StopAllCoroutines();
        ClearOccupants();
        _shotSounds.Clear();
        if (_mine)
        {
            _mine!.SetArmed(false);
            MineDirectional.Mines.Remove(_mine);
        }
        if (_border is CampaignMinefield)
            _border.PlayerShotEvent -= MineEffect;
        else if (_border is CampaignSniperZone)
            _border.PlayerShotEvent -= SniperEffect;
    }

    private void OnDisable() => Clear();

    private void OnDestroy()
    {
        Clear();
        _assets?.Dispose();
        _assets = null;
        _lifetime.Dispose();
        if (_material)
            Destroy(_material);
    }

    private void MineEffect(IObserverToPlayerBridge player, BorderZone zone, float delay, bool hit)
    {
        if (Singleton<Effects>.Instantiated)
            Singleton<Effects>.Instance.EmitGrenade("Grenade_new", _mine ? _mine!.transform.position : player.iPlayer.Position, Vector3.up);
    }

    private void SniperEffect(IObserverToPlayerBridge player, BorderZone zone, float delay, bool hit)
    {
        if (_zone?.Hazard?.PlayShotSound != true || !_assets?.RifleSound)
            return;
        var id = player.iPlayer.ProfileId;
        if (_shotSounds.TryGetValue(id, out var previous))
            StopCoroutine(previous);
        _shotSounds[id] = StartCoroutine(ShotSound(player, delay));
    }

    private IEnumerator ShotSound(IObserverToPlayerBridge player, float delay)
    {
        // Keep the virtual rifle outside the rear of the volume, following its authored rotation.
        var source = transform.TransformPoint(new Vector3(0, _zone!.Size.Y * .5f + 8, -_zone.Size.Z * .5f - 35));
        var sniper = (CampaignSniperZone)_border!;
        var travel = Vector3.Distance(source, player.iPlayer.Position) / sniper.BulletSpeed;
        yield return new WaitForSeconds(Mathf.Max(0, delay - travel));
        if (_cleared || RaidEditor.HazardsSuppressed || player.iPlayer.HealthController?.IsAlive != true)
            yield break;
        var bank = _assets!.RifleSound!;
        if (Singleton<BetterAudio>.Instantiated)
        {
            var distance = CameraManager.Instance.Distance(source);
            var occlusion =
                EnvironmentManager.Instance != null
                && EnvironmentManager.Instance.GetPlayerCurrentEnvironmentType(player.iPlayer.ProfileId) == EnvironmentType.Indoor
                    ? EOcclusionTest.OneShotPropagation
                    : EOcclusionTest.None;
            MonoBehaviourSingleton<BetterAudio>.Instance.PlayAtPointDistant(
                source,
                bank,
                distance,
                1f,
                1f,
                EnvironmentType.Outdoor,
                occlusion
            );
        }
        yield return new WaitForSeconds(travel);
        if (!_cleared && !RaidEditor.HazardsSuppressed && player.iPlayer.IsYourPlayer)
            BulletSoundsUtils.SniperZoneShoot(null);
        _shotSounds.Remove(player.iPlayer.ProfileId);
    }

    private Material VisualMaterial()
    {
        if (!_material)
        {
            _material = new Material(Shader.Find("Standard") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/Internal-Colored"))
            {
                color = new Color(.24f, .27f, .2f),
            };
        }
        return _material!;
    }

    private void BuildClaymore(Transform parent)
    {
        var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.name = "Directional mine marker";
        model.transform.SetParent(parent, false);
        model.transform.localScale = new Vector3(.3f, .15f, .08f);
        var collider = model.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        model.GetComponent<Renderer>().sharedMaterial = VisualMaterial();
    }

    private void BuildWire(SpatialVector size)
    {
        var mesh = _assets!.WireMesh!;
        var bounds = mesh.bounds;
        var count = Mathf.Clamp(Mathf.CeilToInt(size.X / bounds.size.x), 1, 512);
        var width = size.X / count;
        // BSG's spiral_bruno mesh is Z-up; rotate it into the editor's Y-up coordinates.
        var rotation = Quaternion.Euler(-90, 0, 0);
        var scale = new Vector3(width / bounds.size.x, size.Z / bounds.size.y, size.Y / bounds.size.z);
        for (var i = 0; i < count; i++)
        {
            var coil = new GameObject("BSG razor wire");
            coil.transform.SetParent(transform, false);
            coil.transform.localRotation = rotation;
            coil.transform.localScale = scale;
            coil.transform.localPosition =
                new Vector3(-size.X * .5f + width * (i + .5f), 0, 0) - rotation * Vector3.Scale(bounds.center, scale);
            coil.AddComponent<MeshFilter>().sharedMesh = mesh;
            coil.AddComponent<MeshRenderer>().sharedMaterial = _assets.WireMaterial;
        }
    }
}

public sealed class CampaignMinefield : Minefield
{
    internal void Add(Player player) => TargetedPlayers.Add(player);

    internal void Remove(Player player) => TargetedPlayers.Remove(player);
}

public sealed class CampaignSniperZone : SniperFiringZone
{
    internal void Add(Player player) => TargetedPlayers.Add(player);

    internal void Remove(Player player) => TargetedPlayers.Remove(player);
}

public sealed class CampaignBarbedWire : BarbedWire
{
    public override void PlaySound(bool useOcclusion = false)
    {
        if (_soundBank)
            base.PlaySound(useOcclusion);
    }
}

internal static class CampaignWireOccupancy
{
    private static readonly Dictionary<Player, int> Counts = new();

    internal static void Enter(Player player, BarbedWire wire, IObserverToPlayerBridge bridge)
    {
        Counts.TryGetValue(player, out var count);
        Counts[player] = count + 1;
        if (count == 0)
            wire.AddPenalty(bridge);
    }

    internal static void Leave(Player player)
    {
        if (!Counts.TryGetValue(player, out var count))
            return;
        if (count > 1)
            Counts[player] = count - 1;
        else
        {
            Counts.Remove(player);
            if (player)
                player.RemoveStateSpeedLimit(Player.ESpeedLimit.BarbedWire);
        }
    }
}
