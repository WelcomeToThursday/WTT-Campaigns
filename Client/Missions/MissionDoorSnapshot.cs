using EFT.Interactive;
using HarmonyLib;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Client.Missions;

internal sealed class MissionDoorSnapshot
{
    private readonly Door _door;
    private readonly SceneDoorState _state;
    private readonly bool _isBroken;
    private readonly BrokenDoor? _broken;
    private readonly List<(GameObject Object, bool Active)> _objects = new();
    private readonly List<(
        int Index,
        Transform Transform,
        Vector3 Position,
        Quaternion Rotation,
        bool Kinematic,
        float Mass,
        float Drag,
        float AngularDrag,
        bool Gravity,
        RigidbodyConstraints Constraints
    )> _splinters = new();

    internal MissionDoorSnapshot(Door door)
    {
        _door = door;
        _state = new(door);
        _isBroken = door.IsBroken;
        _broken = (BrokenDoor?)AccessTools.Field(typeof(Door), "_broken").GetValue(door);
        if (!_broken)
            return;
        _objects.Add((_broken!.gameObject, _broken.gameObject.activeSelf));
        foreach (var item in _broken.On)
            if (item)
                _objects.Add((item, item.activeSelf));
        foreach (var item in _broken.Off)
            if (item)
                _objects.Add((item, item.activeSelf));
        for (var index = 0; index < _broken.Splinters.Length; index++)
        {
            var body = _broken.Splinters[index];
            if (body)
                _splinters.Add(
                    (
                        index,
                        body.transform,
                        body.transform.localPosition,
                        body.transform.localRotation,
                        body.isKinematic,
                        body.mass,
                        body.drag,
                        body.angularDrag,
                        body.useGravity,
                        body.constraints
                    )
                );
        }
    }

    internal void Restore()
    {
        if (!_door)
            throw new InvalidOperationException("A checkpoint door is unavailable.");
        _door.StopAllCoroutines();
        _door.IsBroken = _isBroken;
        _state.Restore();
        if (!_broken)
            return;
        _broken!.StopAllCoroutines();
        if (_broken.VFX)
            _broken.VFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var saved in _objects)
        {
            if (!saved.Object)
                throw new InvalidOperationException("Checkpoint door geometry was destroyed.");
            saved.Object.SetActive(saved.Active);
        }
        foreach (var saved in _splinters)
        {
            if (!saved.Transform)
                throw new InvalidOperationException("Checkpoint door debris was destroyed.");
            var body = saved.Transform.GetComponent<Rigidbody>() ?? saved.Transform.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            saved.Transform.localPosition = saved.Position;
            saved.Transform.localRotation = saved.Rotation;
            body.mass = saved.Mass;
            body.drag = saved.Drag;
            body.angularDrag = saved.AngularDrag;
            body.useGravity = saved.Gravity;
            body.constraints = saved.Constraints;
            body.isKinematic = saved.Kinematic;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            _broken.Splinters[saved.Index] = body;
        }
        _broken.Init();
    }
}

internal sealed class MissionInteractionSnapshot
{
    private readonly WorldInteractiveObject _target;
    private readonly CheckpointObjectState _state;

    internal MissionInteractionSnapshot(WorldInteractiveObject target)
    {
        _target = target;
        var roots = new List<object>();
        foreach (var name in new[] { "_interaction", "_previousInteraction" })
        {
            var value = AccessTools.Field(typeof(WorldInteractiveObject), name).GetValue(target);
            if (value != null)
                roots.Add(value);
        }
        _state = new(roots, value => value is WorldInteractiveObject.InteractionState);
    }

    internal void Restore()
    {
        _target.StopAllCoroutines();
        _state.Restore();
    }
}
