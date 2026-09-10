using System;
using System.Linq;
using UnityEngine;

namespace WTT.Campaigns.UI.Media;

public sealed class StoryRoomLighting : MonoBehaviour
{
    public Texture2D[] Colors = Array.Empty<Texture2D>();
    public Texture2D[] Directions = Array.Empty<Texture2D>();
    public Texture2D[] Shadows = Array.Empty<Texture2D>();
    public Renderer[] Renderers = Array.Empty<Renderer>();
    public int[] Indices = Array.Empty<int>();
    public Vector4[] Offsets = Array.Empty<Vector4>();
    public StoryEnvironmentState Environment = new();
    private StoryEnvironmentState? _previousEnvironment;
    private LightmapData[]? _previous;
    private LightmapData[]? _combined;

    private void OnEnable() => Apply();

    public void Apply()
    {
        if (_previous != null)
        {
            return;
        }
        _previous = LightmapSettings.lightmaps;
        _previousEnvironment = StoryEnvironmentState.Capture();
        Environment.Apply();
        var added = Colors
            .Select(
                (color, index) =>
                    new LightmapData
                    {
                        lightmapColor = color,
                        lightmapDir = index < Directions.Length ? Directions[index] : null,
                        shadowMask = index < Shadows.Length ? Shadows[index] : null,
                    }
            )
            .ToArray();
        _combined = _previous.Concat(added).ToArray();
        LightmapSettings.lightmaps = _combined;
        for (var index = 0; index < Renderers.Length; index++)
        {
            if (Renderers[index] && Indices[index] >= 0 && Indices[index] < Colors.Length)
            {
                Renderers[index].lightmapIndex = _previous.Length + Indices[index];
                Renderers[index].lightmapScaleOffset = Offsets[index];
            }
        }
    }

    private void OnDisable() => Restore();

    public void Restore()
    {
        var current = LightmapSettings.lightmaps;
        if (
            _previous != null
            && _combined != null
            && current.Length == _combined.Length
            && current.Select(m => m.lightmapColor).SequenceEqual(_combined.Select(m => m.lightmapColor))
        )
        {
            LightmapSettings.lightmaps = _previous;
            _previousEnvironment?.Apply();
        }
        _previous = null;
        _combined = null;
        _previousEnvironment = null;
    }
}
