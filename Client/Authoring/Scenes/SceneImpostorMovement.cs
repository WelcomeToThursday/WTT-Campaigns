using EFT.Impostors;
using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed class SceneImpostorMovement
{
    private static readonly System.Reflection.FieldInfo DrawInstance = AccessTools.Field(typeof(ImpostorsRenderer), "_drawInstance");
    private static readonly System.Reflection.FieldInfo Refreshed = AccessTools.Field(typeof(ImpostorsMainCameraContext), "_refreshed");
    private readonly AmplifyImpostorsArrayElement _element;
    private readonly Matrix4x4[] _matrix = new Matrix4x4[1];
    private readonly ImpostorStruct[] _instance = new ImpostorStruct[1];
    private int _index = -1;

    internal SceneImpostorMovement(AmplifyImpostorsArrayElement element) => _element = element;

    internal void Refresh()
    {
        if (!_element || !ImpostorsRenderer.Instance || DrawInstance.GetValue(ImpostorsRenderer.Instance) is not ImpostorsDrawInstance draw)
            return;
        // Native scene/quality changes may rebuild or reorder the shared instance arrays.
        if (_index < 0 || _index >= draw._impostors.Count || draw._impostors[_index] != _element)
            _index = draw._impostors.IndexOf(_element);
        if (_index < 0 || _index >= draw._localToWorld.Count || _index >= draw._worldToLocal.Count)
            return;
        var transform = _element.transform;
        _matrix[0] = transform.localToWorldMatrix;
        draw._localToWorld[_index] = _matrix[0];
        draw._localToWorldMatrixBuffer?.SetData(_matrix, 0, _index, 1);
        _matrix[0] = transform.worldToLocalMatrix;
        draw._worldToLocal[_index] = _matrix[0];
        draw._worldToLocalMatrixBuffer?.SetData(_matrix, 0, _index, 1);
        for (var camera = 0; camera < draw._camContexts.Count; camera++)
            if (draw._camContexts.GetByIndex(camera) is ImpostorsMainCameraContext context && _index < context._impostorStructs.Count)
            {
                var data = context._impostorStructs[_index];
                var scale = transform.lossyScale;
                data.position = transform.position;
                data.customScale = new Vector2(Mathf.Max(scale.x, scale.z), scale.y);
                data.enabled = _element.isActiveAndEnabled ? 1u : 0u;
                context._impostorStructs[_index] = _instance[0] = data;
                context._impostorsBuffer?.SetData(_instance, 0, _index, 1);
                // Native culling otherwise skips recalculation for a stationary camera.
                Refreshed.SetValue(context, true);
            }
    }
}
