using System;
using System.Linq;
using UnityEngine;

namespace SeasonalPerks.UI.Media;

public sealed class StoryRoomIsolation : MonoBehaviour
{
    private const int RoomMask = 1 << 30;
    private Camera[] _cameras = Array.Empty<Camera>();
    private Light[] _lights = Array.Empty<Light>();
    private ReflectionProbe[] _probes = Array.Empty<ReflectionProbe>();
    private bool _applied;

    private void OnEnable() => Apply();

    public void Apply()
    {
        if (_applied)
            return;
        _applied = true;
        _cameras = FindObjectsOfType<Camera>().Where(c => !c.transform.IsChildOf(transform) && (c.cullingMask & RoomMask) != 0).ToArray();
        _lights = FindObjectsOfType<Light>().Where(l => !l.transform.IsChildOf(transform) && (l.cullingMask & RoomMask) != 0).ToArray();
        _probes = FindObjectsOfType<ReflectionProbe>().Where(p => !p.transform.IsChildOf(transform) && p.enabled).ToArray();
        foreach (var camera in _cameras)
            camera.cullingMask &= ~RoomMask;
        foreach (var light in _lights)
            light.cullingMask &= ~RoomMask;
        foreach (var probe in _probes)
            probe.enabled = false;
    }

    private void OnDisable() => Restore();

    public void Restore()
    {
        // Restore only the bit we changed, retaining unrelated mask changes.
        foreach (var camera in _cameras)
            if (camera)
                camera.cullingMask |= RoomMask;
        foreach (var light in _lights)
            if (light)
                light.cullingMask |= RoomMask;
        foreach (var probe in _probes)
            if (probe)
                probe.enabled = true;
        _cameras = Array.Empty<Camera>();
        _lights = Array.Empty<Light>();
        _probes = Array.Empty<ReflectionProbe>();
        _applied = false;
    }
}
