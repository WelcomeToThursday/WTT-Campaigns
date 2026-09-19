using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Culling shifts are world-space offsets; preserve their relationship to the prop.
internal sealed class SceneLightMovement
{
    private static readonly System.Reflection.FieldInfo PositionCache = AccessTools.Field(
        typeof(CullingObject),
        "_safeMultithreadedPosition"
    );
    private static readonly System.Reflection.FieldInfo Objects = AccessTools.Field(typeof(CullingManager), "_objectsData");
    private readonly CullingLightObject _light;
    private readonly Transform _root;
    private readonly Vector3 _localShift,
        _originalShift;

    internal SceneLightMovement(Transform root, CullingLightObject light)
    {
        _root = root;
        _light = light;
        _originalShift = light.Shift;
        _localShift = root.InverseTransformVector(light.Shift);
    }

    internal void Refresh(bool restoring)
    {
        if (!_light || !_root || !_light.GetTransform())
            return;
        _light.Shift = restoring ? _originalShift : _root.TransformVector(_localShift);
        // The native light update is throttled and can be skipped while autoculled.
        // Refresh the same cache and sphere immediately, including hidden originals.
        PositionCache.SetValue(_light, _light.ClearTransformPosition);
        var manager = CullingManager.Instance;
        if (
            manager
            && Objects.GetValue(manager) is CullingManager.CullingObjectData[] objects
            && _light.Index >= 0
            && _light.Index < objects.Length
            && ReferenceEquals(objects[_light.Index].CullingObject, _light)
        )
            manager.UpdateSphere(_light);
    }
}
