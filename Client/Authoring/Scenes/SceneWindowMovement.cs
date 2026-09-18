using EFT.Interactive;
using HarmonyLib;
using UnityEngine;
using WindowsManagerUtilities;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed class SceneWindowMovement
{
    private static readonly System.Reflection.FieldInfo Breakers = AccessTools.Field(typeof(WindowsManager), "_breakerIdInstanceId");
    private static readonly System.Reflection.FieldInfo RenderData = AccessTools.Field(typeof(WindowsManager), "_renderData");
    private static readonly System.Reflection.FieldInfo Pieces = AccessTools.Field(typeof(WindowBreaker), "_pieces");
    private static readonly System.Reflection.FieldInfo Enabled = AccessTools.Field(typeof(WindowsManager), "_instancesEnable");
    private readonly WindowBreaker _window;
    private readonly Matrix4x4 _originalWorld;
    private Matrix4x4 _previousWorld;
    private WindowsManager? _manager;
    private int _index = -1;
    private Matrix4x4 _originalRender;
    private Vector3 _originalMin,
        _originalMax;
    private readonly Matrix4x4[] _matrix = new Matrix4x4[1];
    private readonly Vector3[] _bounds = new Vector3[2];
    private readonly int[] _enabled = new int[1];
    private int _originalEnabled = 1;

    internal SceneWindowMovement(WindowBreaker window)
    {
        _window = window;
        _originalWorld = _previousWorld = window.transform.localToWorldMatrix;
        Bind();
    }

    private void Bind()
    {
        var manager = WindowsManager.InstanceIsActive() ? WindowsManager.Instance : null;
        if (!manager || ReferenceEquals(manager, _manager))
            return;
        _manager = manager;
        _index = -1;
        if (
            !string.IsNullOrEmpty(_window.Id)
            && Breakers.GetValue(manager) is Dictionary<string, int> ids
            && ids.TryGetValue(_window.Id, out var index)
            && index >= 0
            && index < manager._instancesGeometry._allTransforms.Count
            && index * 2 + 1 < manager._instancesGeometry._allBounds.Count
        )
        {
            _index = index;
            _originalRender = manager._instancesGeometry._allTransforms[index];
            _originalMin = manager._instancesGeometry._allBounds[index * 2];
            _originalMax = manager._instancesGeometry._allBounds[index * 2 + 1];
            if (Enabled.GetValue(manager) is List<int> enabled && index < enabled.Count)
                _originalEnabled = enabled[index];
        }
    }

    internal void Refresh(bool restoring, bool hidden)
    {
        if (!_window)
            return;
        Bind();
        var current = _window.transform.localToWorldMatrix;
        if (_manager && _index >= 0)
        {
            _matrix[0] = restoring ? _originalRender : current * _originalWorld.inverse * _originalRender;
            // Update both the baked CPU data and an already-created GPU buffer.
            _manager!._instancesGeometry._allTransforms[_index] = _matrix[0];
            _bounds[0] = restoring ? _originalMin : _window.Renderer.bounds.min;
            _bounds[1] = restoring ? _originalMax : _window.Renderer.bounds.max;
            _manager._instancesGeometry._allBounds[_index * 2] = _bounds[0];
            _manager._instancesGeometry._allBounds[_index * 2 + 1] = _bounds[1];
            _enabled[0] = hidden || _window.IsDamaged ? 0 : _originalEnabled;
            if (Enabled.GetValue(_manager) is List<int> enabled && _index < enabled.Count)
                enabled[_index] = _enabled[0];
            if (RenderData.GetValue(_manager) is GeometryComputeBuffers data)
            {
                data._allTransformsBuffer?.SetData(_matrix, 0, _index, 1);
                data._allBoundsBuffer?.SetData(_bounds, 0, _index * 2, 2);
                data._instancesEnable?.SetData(_enabled, 0, _index, 1);
            }
        }
        if (current != _previousWorld && _window.FirstHitPosition is { } hit)
        {
            _window.FirstHitPosition = (current * _previousWorld.inverse).MultiplyPoint3x4(hit);
            // Glass broken after placement may still be present during undo/teardown.
            Physics.SyncTransforms();
            if (Pieces.GetValue(_window) is WindowBreaker.Piece[] pieces)
                foreach (var piece in pieces)
                    if (piece.Stuck && piece.Description is { } description && description.GameObject)
                    {
                        description.ChildTransform.position = description.MeshCollider.bounds.center;
                        description.ChildTransform.rotation = Quaternion.identity;
                        description.ChildBoxCollider.size = description.MeshCollider.bounds.size * .8f;
                        if (WindowsManager.InstanceIsActive())
                            WindowsManager.Instance.UpdatePieceTransform(piece.Id, description.GameObject.transform);
                    }
        }
        _previousWorld = current;
    }
}
