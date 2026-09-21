using System.Security.Cryptography;
using System.Text;
using System.Threading;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed partial class MapSceneAdapter
{
    private sealed class Original
    {
        internal Transform Target = null!;
        internal MapTarget Binding = null!;
        internal Vector3 Position,
            LocalPosition,
            LocalScale,
            WorldScale;
        internal Quaternion Rotation,
            LocalRotation;
        internal Transform Parent = null!;
        internal bool Active;
        internal SceneNavigation? Navigation;
        internal readonly List<SceneBodyState> Bodies = new();
        internal bool Applied,
            VisualsDirty,
            Hidden;
        internal HotObject[] Heat = Array.Empty<HotObject>();
        internal StaticDeferredDecal[] Decals = Array.Empty<StaticDeferredDecal>();
        internal StencilShadow[] Shadows = Array.Empty<StencilShadow>();
        internal WorldInteractiveObject[] Interactions = Array.Empty<WorldInteractiveObject>();
        internal readonly List<SceneLightMovement> Lights = new();
        internal WindowBreaker[] Windows = Array.Empty<WindowBreaker>();
        internal TreeInteractive[] Trees = Array.Empty<TreeInteractive>();
        internal VolumetricLight[] Volumes = Array.Empty<VolumetricLight>();
        internal AreaLight[] AreaLights = Array.Empty<AreaLight>();
        internal readonly List<SceneImpostorMovement> Impostors = new();
        internal readonly List<SceneWindowMovement> WindowVisuals = new();

        internal void RefreshVisuals()
        {
            VisualsDirty = false;
            ScenePropMovement.Refresh(Interactions);
            SceneAreaLightMovement.Refresh(AreaLights);
            foreach (var impostor in Impostors)
                impostor.Refresh();
            foreach (var volume in Volumes)
                if (volume && volume.Light && volume.VolumetricMaterial)
                    volume.SetDynamicLightValues();
            foreach (var light in Lights)
                light.Refresh(!Applied);
            foreach (var window in WindowVisuals)
                window.Refresh(!Applied, Hidden);
            foreach (var heat in Heat)
                if (heat)
                    heat.SyncPosition();
            foreach (var shadow in Shadows)
                if (shadow && shadow.Renderer)
                    shadow.Bounds = shadow.Renderer.bounds;
            var renderer = StaticDeferredDecalRenderer.Instance;
            if (renderer)
                foreach (var decal in Decals)
                    if (decal && decal.isActiveAndEnabled)
                    {
                        renderer.UnregisterDecal(decal, true);
                        renderer.RegisterDecal(decal, true);
                    }
        }

        internal void Restore()
        {
            Navigation?.Dispose();
            Navigation = null;
            if (!Applied)
                return;
            Applied = false;
            Hidden = false;
            if (!Target)
                return;
            SceneTreeMovement.ReleaseContacts(Trees);
            // Restoring a world pose through a scaled/rotated parent performs
            // an inverse transform round-trip. Unity can quantize that result
            // by a few ULPs, which changes the exact prop fingerprint when a
            // fresh adapter validates the same target after preview teardown.
            // Restore the captured local transform while the hierarchy is
            // unchanged; keep the world-pose fallback for a moved parent.
            if (Target.parent == Parent)
            {
                Target.localPosition = LocalPosition;
                Target.localRotation = LocalRotation;
                Target.localScale = LocalScale;
            }
            else
            {
                Target.SetPositionAndRotation(Position, Rotation);
                Target.localScale = LocalScale;
            }
            Target.gameObject.SetActive(Active);
            foreach (var state in Bodies)
                state.Restore();
            if (Target.GetComponent<LootItem>() is { } loot)
                loot.RegisterInCullingObject();
            RefreshVisuals();
        }
    }

    private sealed class Spawn
    {
        internal string Definition = "";
        internal GameObject? Model;
        internal SceneModelLease<GameObject>? Lease;
        internal SceneModelLease<SceneAssetCatalog.Model>? AssetLease;
        internal bool Pending => Lease?.Pending == true || AssetLease?.Pending == true;
        internal string Error => AssetLease?.Error ?? Lease?.Error ?? "";
        internal SpatialCapture Pose = null!;
        internal SceneNavigation? Navigation;

        internal void Dispose()
        {
            Navigation?.Dispose();
            Navigation = null;
            Lease?.Dispose();
            AssetLease?.Dispose();
            if (Lease == null && AssetLease == null && Model)
                Remove(Model!);
            Model = null;
        }
    }

    private readonly Dictionary<string, Original> _originals = new();
    private readonly Dictionary<string, Spawn> _spawns = new();
    private readonly Dictionary<string, SceneDoorState> _doors = new();
    private readonly Dictionary<string, SceneDoorPlacement> _placedDoors = new();
    private bool _disposed;
    internal bool Loading => _spawns.Values.AsValueEnumerable().Any(s => s.Pending);

    internal bool IsHidden(GameObject target)
    {
        foreach (var original in _originals.Values)
            if (original.Applied && original.Hidden && original.Target && original.Target.gameObject == target)
                return true;
        return false;
    }

    private static string Key(MapTarget t) => t.Kind + ":" + t.Scene + ":" + t.Path + ":" + t.NativeId + ":" + t.Fingerprint;

    internal static Transform? Root(Transform? hit)
    {
        if (!hit || hit!.GetComponentInParent<Player>() || hit.GetComponentInParent<Canvas>())
            return null;
        var loot = hit.GetComponentInParent<LootItem>();
        if (loot)
            return loot.transform;
        var container = hit.GetComponentInParent<LootableContainer>();
        if (container)
            return container.transform;
        var body = hit.GetComponentInParent<Rigidbody>();
        var visual = body ? body.transform : WTT.Campaigns.UI.Controls.SceneSelectionGeometry.VisualRoot(hit);
        return Supported(visual).Length == 0 ? visual : null;
    }

    private static string NativeSupported(Transform t)
    {
        if (t.GetComponentInParent<Player>() || t.GetComponentInChildren<Door>(true))
            return "This object belongs to gameplay infrastructure.";
        if (t.GetComponent<LootItem>() is { } loot && (loot.Item == null || loot.Item.QuestItem || loot.GetType() != typeof(LootItem)))
            return "Quest items, corpses and specialized loot are unavailable.";
        if (
            t.GetComponent<LootableContainer>() is { } container
            && (container.GetType() != typeof(LootableContainer) || string.IsNullOrEmpty(container.Id))
        )
            return "This container has no supported stable identity.";
        if (t.GetComponentsInChildren<Renderer>(true).AsValueEnumerable().Any(r => r.isPartOfStaticBatch))
            return "Combined static geometry cannot be moved safely.";
        return "";
    }

    private static MapTarget CaptureNative(Transform t)
    {
        var loot = t.GetComponent<LootItem>();
        var container = t.GetComponent<LootableContainer>();
        var target = new MapTarget
        {
            Kind = loot ? "Loot" : "Container",
            Scene = t.gameObject.scene.name,
            Path = PathOf(t),
            NativeId = loot ? loot.StaticId ?? "" : container.Id,
            Template = loot ? loot.TemplateId : container.Template ?? "",
            Origin = ZoneRuntime.Vector(t.position),
        };
        var evidence =
            target.Kind
            + "|"
            + target.Template
            + "|"
            + target.NativeId
            + "|"
            + t.GetComponentsInChildren<MeshFilter>(true)
                .AsValueEnumerable()
                .Select(f => f.sharedMesh ? f.sharedMesh.name + ":" + f.sharedMesh.vertexCount : "missing")
                .JoinToString("|");
        using var hash = SHA256.Create();
        target.Fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(evidence))).Replace("-", "");
        return target;
    }

    private static Transform ResolveNative(MapTarget target)
    {
        var candidates =
            target.Kind == "Loot"
                ? Resources.FindObjectsOfTypeAll<LootItem>().AsValueEnumerable().Select(x => x.transform).ToArray()
                : Resources.FindObjectsOfTypeAll<LootableContainer>().AsValueEnumerable().Select(x => x.transform).ToArray();
        var matches = candidates
            .AsValueEnumerable()
            .Where(t => t && t.gameObject.scene.IsValid() && t.gameObject.scene.name == target.Scene)
            .Where(t =>
            {
                // Reject unrelated loot before traversing meshes and hashing its
                // fingerprint. A map can contain thousands of native loot items.
                var loot = t.GetComponent<LootItem>();
                var container = t.GetComponent<LootableContainer>();
                var nativeId = loot ? loot.StaticId ?? "" : container.Id;
                var template = loot ? loot.TemplateId : container.Template ?? "";
                if (template != target.Template || (target.NativeId.Length > 0 && target.NativeId != nativeId))
                    return false;
                if (target.NativeId.Length == 0 && target.Kind == "Loot")
                {
                    if (target.Origin?.Finite != true || (t.position - ZoneRuntime.Vector(target.Origin)).sqrMagnitude >= .15f * .15f)
                        return false;
                }
                var actual = CaptureNative(t);
                return SceneTargetRules.Matches(target, actual);
            })
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("Missing or ambiguous " + target.Kind.ToLowerInvariant() + "; rebind " + target.Path);
        var error = NativeSupported(matches[0]);
        if (error.Length > 0)
            throw new InvalidOperationException(error);
        return matches[0];
    }

    internal MapTarget CaptureOriginal(Transform t)
    {
        var existing = _originals.Values.AsValueEnumerable().FirstOrDefault(o => o.Target == t);
        if (existing != null)
            return RaidEditorSession.Copy(existing.Binding);
        return Capture(t);
    }

    internal string? RecordAt(Transform t)
    {
        foreach (var pair in _placedDoors)
            if (pair.Value.Root && t.IsChildOf(pair.Value.Root.transform))
                return pair.Key;
        foreach (var pair in _spawns)
            if (pair.Value.Model && (t == pair.Value.Model!.transform || t.IsChildOf(pair.Value.Model.transform)))
                return pair.Key;
        return null;
    }

    internal IEnumerable<Renderer> SpawnRenderers()
    {
        foreach (var placed in _placedDoors.Values)
            if (placed.Root)
                foreach (var renderer in placed.Root.GetComponentsInChildren<Renderer>())
                    yield return renderer;
        foreach (var spawn in _spawns.Values)
            if (spawn.Model)
                foreach (var renderer in spawn.Model!.GetComponentsInChildren<Renderer>())
                    yield return renderer;
    }

    internal Transform? OriginalFor(MapTarget binding) => _originals.TryGetValue(Key(binding), out var original) ? original.Target : null;

    internal void RefreshVisuals(Transform target)
    {
        foreach (var original in _originals.Values)
            if (original.Target == target)
            {
                original.VisualsDirty = true;
                return;
            }
    }

    internal void FlushVisuals()
    {
        foreach (var original in _originals.Values)
            if (original.Target && original.VisualsDirty)
                original.RefreshVisuals();
    }

    internal GameObject CopyForPlacement(Transform t) => CopyProp(t, false, t.GetComponent<Door>());

    internal Transform? TargetFor(string id, MapObjectEdit? edit)
    {
        if (_placedDoors.TryGetValue(id, out var door) && door.Root)
            return door.Root.transform;
        if (_spawns.TryGetValue(id, out var spawn) && spawn.Model)
            return spawn.Model!.transform;
        if (edit != null && _originals.TryGetValue(Key(edit.Target), out var original) && original.Target)
            return original.Target;
        return null;
    }

    internal Transform? SelectionGeometryFor(string id) =>
        _spawns.TryGetValue(id, out var spawn) ? spawn.AssetLease?.Model?.SelectionGeometry : null;

    internal IEnumerable<(string Id, WorldInteractiveObject Object)> MissionInteractions(MapLayout layout)
    {
        foreach (var edit in layout.Doors)
        {
            if (_placedDoors.TryGetValue(edit.Id, out var placed))
                yield return (edit.Id, placed.State.Door);
            else if (_doors.TryGetValue(Key(edit.Target), out var state))
                yield return (edit.Id, state.Door);
        }
        foreach (var edit in layout.Objects)
        {
            var target = TargetFor(edit.Id, edit);
            var container = target ? target!.GetComponentInChildren<LootableContainer>(true) : null;
            if (container)
                yield return (edit.Id, container);
        }
    }

    internal void Reconcile(MapLayout? layout, MapObjectEdit? preview = null, bool runtime = false)
    {
        if (_disposed || (!EditorMode.Ready && !runtime))
            return;
        TargetErrors.Clear();
        var edits = layout?.Objects.AsValueEnumerable().ToList() ?? new List<MapObjectEdit>();
        if (preview != null)
            edits.Add(preview);
        var bindings = edits.AsValueEnumerable().Select(e => Key(e.Target)).ToHashSet();
        foreach (var old in _originals.Keys.AsValueEnumerable().Where(k => !bindings.Contains(k)).ToArray())
        {
            _originals[old].Restore();
            _originals.Remove(old);
        }
        var needed = new HashSet<string>();
        var targets = new HashSet<string>();
        var doorIds = new HashSet<string>();
        if (layout != null)
        {
            foreach (var edit in edits)
                try
                {
                    if (edit.Target.IsAsset || SceneAssetRules.IsContainer(edit))
                    {
                        if (
                            edit.Operation != "Copy"
                            || !MapLayoutRules.Positive(edit.Scale)
                            || (SceneAssetRules.IsContainer(edit) && ZoneRuntime.Vector(edit.Scale) != Vector3.one)
                        )
                            throw new InvalidOperationException("Invalid asset placement or unsupported container scale.");
                        if (runtime && SceneAssetRules.IsContainer(edit))
                            continue;
                        needed.Add(edit.Id);
                        var signature = JsonConvert.SerializeObject(edit.Target);
                        if (_spawns.TryGetValue(edit.Id, out var previous) && previous.Definition != signature)
                        {
                            previous.Dispose();
                            _spawns.Remove(edit.Id);
                        }
                        if (!_spawns.TryGetValue(edit.Id, out var assetSpawn))
                        {
                            assetSpawn = new Spawn
                            {
                                Definition = signature,
                                Pose = edit,
                                AssetLease = new(m => m.Dispose()),
                            };
                            _spawns.Add(edit.Id, assetSpawn);
                            _ = LoadAsset(assetSpawn, edit);
                        }
                        assetSpawn.Pose = edit;
                        if (assetSpawn.Model)
                        {
                            var restriction = ScaleRestriction(assetSpawn.Model!.transform);
                            if (ZoneRuntime.Vector(edit.Scale) != Vector3.one && restriction.Length > 0)
                                throw new InvalidOperationException(restriction);
                            Pose(assetSpawn.Model!.transform, edit, true);
                            assetSpawn.Navigation ??= new SceneNavigation(assetSpawn.Model.transform);
                        }
                        if (assetSpawn.Error.Length > 0)
                            TargetErrors.Add(edit.Name + ": " + assetSpawn.Error);
                        continue;
                    }
                    var key = Key(edit.Target);
                    if (!_originals.TryGetValue(key, out var original))
                    {
                        var t = Resolve(edit.Target, false);
                        original = new Original
                        {
                            Target = t,
                            Binding = RaidEditorSession.Copy(edit.Target),
                            Position = t.position,
                            LocalPosition = t.localPosition,
                            LocalScale = t.localScale,
                            WorldScale = t.lossyScale,
                            Rotation = t.rotation,
                            LocalRotation = t.localRotation,
                            Parent = t.parent,
                            Active = t.gameObject.activeSelf,
                            Heat = t.GetComponentsInChildren<HotObject>(true),
                            Decals = t.GetComponentsInChildren<StaticDeferredDecal>(true),
                            Shadows = t.GetComponentsInChildren<StencilShadow>(true),
                            Interactions = t.GetComponentsInChildren<WorldInteractiveObject>(true),
                            Windows = t.GetComponentsInChildren<WindowBreaker>(true),
                            Trees = t.GetComponentsInChildren<TreeInteractive>(true),
                            Volumes = t.GetComponentsInChildren<VolumetricLight>(true),
                            AreaLights = t.GetComponentsInChildren<AreaLight>(true),
                        };
                        foreach (var light in t.GetComponentsInChildren<CullingLightObject>(true))
                            original.Lights.Add(new SceneLightMovement(t, light));
                        foreach (var window in original.Windows)
                            original.WindowVisuals.Add(new SceneWindowMovement(window));
                        foreach (var impostor in t.GetComponentsInChildren<EFT.Impostors.AmplifyImpostorsArrayElement>(true))
                            original.Impostors.Add(new SceneImpostorMovement(impostor));
                        foreach (var body in t.GetComponentsInChildren<Rigidbody>(true))
                            original.Bodies.Add(new SceneBodyState(body));
                        _originals.Add(key, original);
                    }
                    if (!original.Target)
                        throw new InvalidOperationException("Target was unloaded; rebind " + edit.Name);
                    // Reject saved/remote edits too, before spawning, activating or changing any transform.
                    if (
                        edit.Operation != "Hide"
                        && edit.Target.Kind == "Prop"
                        && ZoneRuntime.Vector(edit.Scale) != original.WorldScale
                        && ScaleRestriction(original.Target) is { Length: > 0 } scaleError
                    )
                        throw new InvalidOperationException(scaleError);
                    if (edit.Operation == "Copy")
                    {
                        needed.Add(edit.Id);
                        var signature = JsonConvert.SerializeObject(edit.Target);
                        if (_spawns.TryGetValue(edit.Id, out var prior) && prior.Definition != signature)
                        {
                            prior.Dispose();
                            _spawns.Remove(edit.Id);
                        }
                        if (!_spawns.TryGetValue(edit.Id, out var spawn))
                            _spawns.Add(edit.Id, spawn = new Spawn { Definition = signature, Model = CopyProp(original.Target, true) });
                        Pose(spawn.Model!.transform, edit, true);
                        spawn.Navigation ??= new SceneNavigation(spawn.Model.transform);
                    }
                    else
                    {
                        targets.Add(key);
                        if (!original.Applied && original.Target.GetComponent<LootItem>() is { } loot)
                            loot.UnregisterFromCullingObject();
                        original.Applied = true;
                        if (original.Hidden != (edit.Operation == "Hide"))
                        {
                            SceneTreeMovement.ReleaseContacts(original.Trees);
                            original.VisualsDirty = true;
                        }
                        original.Hidden = edit.Operation == "Hide";
                        foreach (var body in original.Bodies)
                            body.Freeze();
                        original.Target.gameObject.SetActive(edit.Operation != "Hide" && original.Active);
                        if (edit.Operation == "Hide")
                        {
                            original.Navigation?.Dispose();
                            original.Navigation = null;
                        }
                        if (edit.Operation == "Move")
                        {
                            var position = original.Target.position;
                            var rotation = original.Target.rotation;
                            var scale = original.Target.lossyScale;
                            if (
                                position != ZoneRuntime.Vector(edit.Position)
                                || rotation != Quaternion.Euler(ZoneRuntime.Vector(edit.Rotation))
                                || scale != ZoneRuntime.Vector(edit.Scale)
                            )
                                SceneTreeMovement.ReleaseContacts(original.Trees);
                            // Reapplying an unchanged runtime layout must not reset a shot window.
                            if (
                                position != ZoneRuntime.Vector(edit.Position)
                                || rotation != Quaternion.Euler(ZoneRuntime.Vector(edit.Rotation))
                            )
                                foreach (var window in original.Windows)
                                    if (window && (window.IsDamaged || window.HasPieces))
                                        throw new InvalidOperationException(
                                            "This window broke after selection; its loose pieces cannot be relocated."
                                        );
                            Pose(original.Target, edit, false);
                            if (edit.Target.Kind == "Prop" && ScaleRestriction(original.Target).Length == 0)
                                WTT.Campaigns.UI.Controls.SceneSelectionGeometry.WorldScale(
                                    original.Target,
                                    ZoneRuntime.Vector(edit.Scale)
                                );
                            if (
                                position != original.Target.position
                                || rotation != original.Target.rotation
                                || scale != original.Target.lossyScale
                            )
                                original.VisualsDirty = true;
                            original.Navigation ??= new SceneNavigation(original.Target);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (_originals.TryGetValue(Key(edit.Target), out var failed))
                        failed.Restore();
                    TargetErrors.Add(edit.Name + ": " + e.Message);
                }
            // Editor layouts use preview models while a mission runtime owns
            // native loot activation and cleanup through MissionLoot.
            if (!runtime)
                foreach (var loot in layout.Loot)
                {
                    needed.Add(loot.Id);
                    var signature = JsonConvert.SerializeObject(loot.Items);
                    if (_spawns.TryGetValue(loot.Id, out var old) && old.Definition != signature)
                    {
                        old.Dispose();
                        _spawns.Remove(loot.Id);
                    }
                    if (!_spawns.TryGetValue(loot.Id, out var spawn))
                    {
                        spawn = new Spawn
                        {
                            Definition = signature,
                            Lease = new(SceneLootModel.Release),
                            Pose = loot,
                        };
                        _spawns.Add(loot.Id, spawn);
                        _ = Load(spawn, loot);
                    }
                    spawn.Pose = loot;
                    if (spawn.Model)
                        SetPose(spawn.Model!.transform, loot);
                    if (spawn.Error.Length > 0)
                        TargetErrors.Add(loot.Name + ": " + spawn.Error);
                }
            foreach (var edit in layout.Doors)
                try
                {
                    if (edit.PlaceNew)
                    {
                        var signature = JsonConvert.SerializeObject(edit.Target);
                        if (_placedDoors.TryGetValue(edit.Id, out var old) && old.Source != signature)
                        {
                            old.Dispose();
                            _placedDoors.Remove(edit.Id);
                        }
                        if (!_placedDoors.TryGetValue(edit.Id, out var placed))
                            _placedDoors.Add(edit.Id, placed = new SceneDoorPlacement(Resolve(edit.Target, true), edit));
                        placed.Pose(edit);
                        placed.State.Apply(edit);
                        continue;
                    }
                    var key = Key(edit.Target);
                    doorIds.Add(key);
                    if (!_doors.TryGetValue(key, out var state))
                    {
                        var door = Resolve(edit.Target, true).GetComponent<Door>();
                        _doors.Add(key, state = new SceneDoorState(door));
                    }
                    state.Apply(edit);
                }
                catch (Exception e)
                {
                    TargetErrors.Add(edit.Name + ": " + e.Message);
                }
        }
        foreach (var original in _originals)
            if (!targets.Contains(original.Key))
                original.Value.Restore();
        foreach (var id in _spawns.Keys.AsValueEnumerable().Where(id => !needed.Contains(id)).ToArray())
        {
            _spawns[id].Dispose();
            _spawns.Remove(id);
        }
        foreach (var id in _doors.Keys.AsValueEnumerable().Where(id => !doorIds.Contains(id)).ToArray())
        {
            _doors[id].Restore();
            _doors.Remove(id);
        }
        Physics.SyncTransforms();
        foreach (
            var id in _placedDoors
                .Keys.AsValueEnumerable()
                .Where(id => layout?.Doors.AsValueEnumerable().Any(d => d.PlaceNew && d.Id == id) != true)
                .ToArray()
        )
        {
            _placedDoors[id].Dispose();
            _placedDoors.Remove(id);
        }
    }

    private async Task LoadAsset(Spawn spawn, MapObjectEdit edit)
    {
        await spawn.AssetLease!.Load(async token =>
        {
            var model = await SceneAssetCatalog.Load(edit.Target, token);
            try
            {
                var pose = (MapObjectEdit)spawn.Pose;
                var restriction = ScaleRestriction(model.Object.transform);
                if (ZoneRuntime.Vector(pose.Scale) != Vector3.one && restriction.Length > 0)
                    throw new InvalidOperationException(restriction);
                return model;
            }
            catch
            {
                model.Dispose();
                throw;
            }
        });
        if (_disposed)
            return;
        spawn.Model = spawn.AssetLease.Model?.Object;
        if (spawn.Model)
        {
            if (spawn.Model!.GetComponentInChildren<LootableContainer>(true) is { } container)
                container.enabled = false;
            Pose(spawn.Model.transform, (MapObjectEdit)spawn.Pose, true);
            spawn.Model.SetActive(true);
        }
    }

    private async Task Load(Spawn spawn, MapLootPlacement loot)
    {
        await spawn.Lease!.Load(token => SceneLootModel.Create(loot.Items, token));
        if (_disposed)
            return;
        spawn.Model = spawn.Lease.Model;
        if (spawn.Model)
            SetPose(spawn.Model!.transform, spawn.Pose);
    }

    private static void SetPose(Transform t, SpatialCapture point) =>
        t.SetPositionAndRotation(ZoneRuntime.Vector(point.Position), Quaternion.Euler(ZoneRuntime.Vector(point.Rotation)));

    private void DisposeEdits()
    {
        _disposed = true;
        var cleanup = new SceneEditTransaction();
        foreach (var spawn in _spawns.Values)
            cleanup.Apply(() => { }, spawn.Dispose);
        _spawns.Clear();
        foreach (var original in _originals.Values)
            cleanup.Apply(() => { }, original.Restore);
        _originals.Clear();
        foreach (var door in _doors.Values)
            cleanup.Apply(() => { }, door.Restore);
        _doors.Clear();
        foreach (var door in _placedDoors.Values)
            cleanup.Apply(() => { }, door.Dispose);
        _placedDoors.Clear();
        cleanup.Dispose();
    }
}
