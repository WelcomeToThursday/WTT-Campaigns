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

namespace WTT.Campaigns.Client.Authoring;

internal sealed partial class MapSceneAdapter
{
    private sealed class Original
    {
        internal Transform Target = null!;
        internal MapTarget Binding = null!;
        internal Vector3 Position,
            LocalScale,
            WorldScale;
        internal Quaternion Rotation;
        internal bool Active;
        internal readonly List<SceneBodyState> Bodies = new();
        internal bool Applied,
            VisualsDirty,
            Hidden;
        internal HotObject[] Heat = Array.Empty<HotObject>();
        internal StaticDeferredDecal[] Decals = Array.Empty<StaticDeferredDecal>();
        internal StencilShadow[] Shadows = Array.Empty<StencilShadow>();

        internal void RefreshVisuals()
        {
            VisualsDirty = false;
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
            if (!Applied)
                return;
            Applied = false;
            Hidden = false;
            if (!Target)
                return;
            Target.SetPositionAndRotation(Position, Rotation);
            Target.localScale = LocalScale;
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
        internal bool Pending => Lease?.Pending == true;
        internal string Error => Lease?.Error ?? "";
        internal SpatialCapture Pose = null!;

        internal void Dispose()
        {
            Lease?.Dispose();
            if (Lease == null && Model)
                Remove(Model!);
            Model = null;
        }
    }

    private readonly Dictionary<string, Original> _originals = new();
    private readonly Dictionary<string, Spawn> _spawns = new();
    private readonly Dictionary<string, (Door Door, EDoorState State, float Angle)> _doors = new();
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
        foreach (var pair in _spawns)
            if (pair.Value.Model && (t == pair.Value.Model!.transform || t.IsChildOf(pair.Value.Model.transform)))
                return pair.Key;
        return null;
    }

    internal IEnumerable<Renderer> SpawnRenderers()
    {
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

    internal GameObject CopyForPlacement(Transform t) => CopyProp(t, false);

    internal Transform? TargetFor(string id, MapObjectEdit? edit)
    {
        if (_spawns.TryGetValue(id, out var spawn) && spawn.Model)
            return spawn.Model!.transform;
        if (edit != null && _originals.TryGetValue(Key(edit.Target), out var original) && original.Target)
            return original.Target;
        return null;
    }

    internal void Reconcile(MapLayout? layout, MapObjectEdit? preview = null)
    {
        if (_disposed || !EditorMode.Ready)
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
                    var key = Key(edit.Target);
                    if (!_originals.TryGetValue(key, out var original))
                    {
                        var t = Resolve(edit.Target, false);
                        original = new Original
                        {
                            Target = t,
                            Binding = RaidEditorSession.Copy(edit.Target),
                            Position = t.position,
                            LocalScale = t.localScale,
                            WorldScale = t.lossyScale,
                            Rotation = t.rotation,
                            Active = t.gameObject.activeSelf,
                            Heat = t.GetComponentsInChildren<HotObject>(true),
                            Decals = t.GetComponentsInChildren<StaticDeferredDecal>(true),
                            Shadows = t.GetComponentsInChildren<StencilShadow>(true),
                        };
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
                    }
                    else
                    {
                        targets.Add(key);
                        if (!original.Applied && original.Target.GetComponent<LootItem>() is { } loot)
                            loot.UnregisterFromCullingObject();
                        original.Applied = true;
                        original.Hidden = edit.Operation == "Hide";
                        foreach (var body in original.Bodies)
                            body.Freeze();
                        original.Target.gameObject.SetActive(edit.Operation != "Hide" && original.Active);
                        if (edit.Operation == "Move")
                        {
                            var position = original.Target.position;
                            var rotation = original.Target.rotation;
                            var scale = original.Target.lossyScale;
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
                        }
                    }
                }
                catch (Exception e)
                {
                    if (_originals.TryGetValue(Key(edit.Target), out var failed))
                        failed.Restore();
                    TargetErrors.Add(edit.Name + ": " + e.Message);
                }
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
                    var key = Key(edit.Target);
                    doorIds.Add(key);
                    if (!_doors.TryGetValue(key, out var state))
                    {
                        var door = Resolve(edit.Target, true).GetComponent<Door>();
                        _doors.Add(key, state = (door, door.DoorState, door.CurrentAngle));
                    }
                    var next = edit.State == "Unchanged" ? state.State : (EDoorState)Enum.Parse(typeof(EDoorState), edit.State);
                    state.Door.SetInitialSyncState(
                        new WorldInteractiveObject.InteractiveObjectStatusInfo(state.Door.Id, next, state.Door.GetAngle(next))
                    );
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
            RestoreDoor(_doors[id]);
            _doors.Remove(id);
        }
        Physics.SyncTransforms();
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

    private static void RestoreDoor((Door Door, EDoorState State, float Angle) state)
    {
        if (!state.Door)
            return;
        state.Door.SetInitialSyncState(new WorldInteractiveObject.InteractiveObjectStatusInfo(state.Door.Id, state.State, state.Angle));
        state.Door.CurrentAngle = state.Angle;
    }

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
            cleanup.Apply(() => { }, () => RestoreDoor(door));
        _doors.Clear();
        cleanup.Dispose();
    }
}
