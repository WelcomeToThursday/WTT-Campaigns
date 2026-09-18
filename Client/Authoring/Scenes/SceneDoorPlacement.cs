using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using RootMotion.FinalIK;
using UnityEngine;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed class SceneDoorPlacement : IDisposable
{
    private static readonly System.Reflection.FieldInfo Registry = AccessTools.Field(typeof(World), "_interactiveObjectsDictionary");
    internal readonly GameObject Root;
    internal readonly SceneDoorState State;
    internal readonly string Source;
    private readonly GameWorld _world;
    private readonly SceneDoorRegistry _registry;

    // Diagnostic overlays belong to the inspected original, never to an authored copy.
    private static bool Diagnostic(Component c) => c && c.GetType().FullName == "DebugPlus.Utils.OverlayProvider";

    internal static string Restriction(Transform source)
    {
        var door = source.GetComponent<Door>();
        if (!door || door.GetType() != typeof(Door) || door.IsBroken)
            return "Choose an intact standard native door.";
        bool Outside(Component part) => part && !part.transform.IsChildOf(source);
        if (Outside(door.LockHandle) || Outside(door._handle) || Outside(door.Collider))
            return "This door's handles or collider belong to a separate assembly; it cannot be placed independently.";
        if (door._mboitRenderers is { Length: > 0 })
            return "This glass door uses baked map rendering and cannot be copied as an independent door.";
        var count = 0;
        foreach (var c in source.GetComponentsInChildren<Component>(true))
        {
            if (++count > 256)
                return "Select an individual door.";
            if (Diagnostic(c))
                continue;
            if (c is SmartGrip grip && (Outside(grip.Pivot) || Outside(grip.Targets) || Outside(grip.DEBUG_TARGET)))
                return "This door's hand animation references another assembly.";
            if (c is LimbIK limb)
            {
                var solver = limb.solver;
                if (
                    Outside(solver.target)
                    || Outside(solver.bendGoal)
                    || Outside(solver.bone1.transform)
                    || Outside(solver.bone2.transform)
                    || Outside(solver.bone3.transform)
                )
                    return "This door's finger animation references another assembly.";
                continue;
            }
            if (
                c
                is Transform
                    or MeshFilter
                    or MeshRenderer
                    or BoxCollider
                    or SphereCollider
                    or CapsuleCollider
                    or MeshCollider
                    or DoorHandle
                    or GripPose
                    or AudioSource
                    or ParticleSystem
                    or ParticleSystemRenderer
                    or OcclusionPortal
                    or UnityEngine.AI.NavMeshObstacle
            )
                continue;
            if (c == door || c && c.GetType().FullName == "EFT.Ballistics.BallisticCollider")
                continue;
            return "Door placement needs support for " + (c ? c.GetType().FullName : "a missing component") + ".";
        }
        return "";
    }

    internal SceneDoorPlacement(Transform source, MapDoorEdit edit)
    {
        var error = Restriction(source);
        if (error.Length > 0)
            throw new InvalidOperationException(error);
        _world = Singleton<GameWorld>.Instance;
        if (!_world)
            throw new InvalidOperationException("The game world is not ready.");
        var networkWorld = _world.World;
        _registry = new SceneDoorRegistry(networkWorld ? networkWorld : null, Registry, _world.IsLocalGame());
        var id = "wtt-door-" + edit.Id;
        if (_registry.Contains(id))
            throw new InvalidOperationException("A placed door already owns " + id);
        Source = Newtonsoft.Json.JsonConvert.SerializeObject(edit.Target);
        Root = new GameObject("CampaignEditor placed door " + edit.Id);
        Root.SetActive(false);
        try
        {
            var clone = UnityEngine.Object.Instantiate(source.gameObject, Root.transform, false);
            clone.name = source.name;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localScale = source.lossyScale;
            // Map culling may have disabled the source; it does not own this copy.
            foreach (var renderer in clone.GetComponentsInChildren<MeshRenderer>(true))
                renderer.enabled = true;
            var door = clone.GetComponent<Door>();
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
                if (Diagnostic(component))
                    UnityEngine.Object.DestroyImmediate(component);
            door.Id = id;
            door.TriggersMap = Array.Empty<WorldInteractiveObject.TriggerOnStatePair>();
            door._mboitRenderers = Array.Empty<WorldInteractiveObject.MBOITRenderer>();
            door._occlusionPortal = null;
            door.Obstacle = null;
            foreach (var portal in clone.GetComponentsInChildren<OcclusionPortal>(true))
                UnityEngine.Object.DestroyImmediate(portal);
            foreach (var obstacle in clone.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true))
                UnityEngine.Object.DestroyImmediate(obstacle);
            door.enabled = true;
            door.DoorState = EDoorState.Shut;
            clone.SetActive(true);
            Pose(edit);
            Root.SetActive(true);
            State = new SceneDoorState(door);
            State.Apply(edit);
            if (_registry.Available)
                _world.RegisterWorldInteractionObject(door);
            door.OnDoorStateChanged += _world.WorldInteractiveObjectOnDoorStateChanged;
        }
        catch
        {
            Root.SetActive(false);
            UnityEngine.Object.Destroy(Root);
            throw;
        }
    }

    internal void Pose(MapDoorEdit edit) =>
        Root.transform.SetPositionAndRotation(
            new Vector3(edit.Position.X, edit.Position.Y, edit.Position.Z),
            Quaternion.Euler(edit.Rotation.X, edit.Rotation.Y, edit.Rotation.Z)
        );

    public void Dispose()
    {
        if (State.Door)
        {
            State.Door.OnDoorStateChanged -= _world.WorldInteractiveObjectOnDoorStateChanged;
            if (_world)
                _world.WorldInteractiveObjectOnDoorStateChanged(State.Door, EDoorState.Interacting, EDoorState.Shut);
            _registry.Remove(State.Door.Id, State.Door);
        }
        if (Root)
        {
            Root.SetActive(false);
            UnityEngine.Object.Destroy(Root);
        }
    }
}
