using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

internal sealed class SceneBodyState
{
    private readonly Rigidbody _body;
    private readonly bool _kinematic,
        _gravity,
        _sleeping;
    private readonly Vector3 _velocity,
        _angular;

    internal SceneBodyState(Rigidbody body)
    {
        _body = body;
        _kinematic = body.isKinematic;
        _gravity = body.useGravity;
        _sleeping = body.IsSleeping();
        _velocity = body.velocity;
        _angular = body.angularVelocity;
    }

    internal void Freeze()
    {
        if (!_body)
            return;
        _body.isKinematic = true;
        _body.useGravity = false;
    }

    internal void Restore()
    {
        if (!_body)
            return;
        _body.isKinematic = _kinematic;
        _body.useGravity = _gravity;
        if (!_kinematic)
        {
            _body.velocity = _velocity;
            _body.angularVelocity = _angular;
            if (_sleeping)
                _body.Sleep();
            else
                _body.WakeUp();
        }
    }
}
