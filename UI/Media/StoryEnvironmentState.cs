using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeasonalPerks.UI.Media;

[Serializable]
public sealed class StoryEnvironmentState
{
    public AmbientMode AmbientMode;
    public Color AmbientSky;
    public Color AmbientEquator;
    public Color AmbientGround;
    public float AmbientIntensity;
    public float[] AmbientProbe = Array.Empty<float>();
    public bool Fog;
    public FogMode FogMode;
    public Color FogColor;
    public float FogDensity;
    public float FogStart;
    public float FogEnd;
    public Material? Skybox;
    public DefaultReflectionMode ReflectionMode;
    public Texture? Reflection;
    public float ReflectionIntensity;
    public int ReflectionBounces;
    public LightmapsMode LightmapsMode;

    public static StoryEnvironmentState Capture()
    {
        return new StoryEnvironmentState
        {
            AmbientMode = RenderSettings.ambientMode,
            AmbientSky = RenderSettings.ambientSkyColor,
            AmbientEquator = RenderSettings.ambientEquatorColor,
            AmbientGround = RenderSettings.ambientGroundColor,
            AmbientIntensity = RenderSettings.ambientIntensity,
            AmbientProbe = CaptureProbe(),
            Fog = RenderSettings.fog,
            FogMode = RenderSettings.fogMode,
            FogColor = RenderSettings.fogColor,
            FogDensity = RenderSettings.fogDensity,
            FogStart = RenderSettings.fogStartDistance,
            FogEnd = RenderSettings.fogEndDistance,
            Skybox = RenderSettings.skybox,
            ReflectionMode = RenderSettings.defaultReflectionMode,
            Reflection = RenderSettings.customReflectionTexture,
            ReflectionIntensity = RenderSettings.reflectionIntensity,
            ReflectionBounces = RenderSettings.reflectionBounces,
            LightmapsMode = LightmapSettings.lightmapsMode,
        };
    }

    public void Apply()
    {
        RenderSettings.ambientMode = AmbientMode;
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
        RenderSettings.ambientIntensity = AmbientIntensity;
        if (AmbientProbe?.Length == 27)
        {
            var probe = new SphericalHarmonicsL2();
            for (var channel = 0; channel < 3; channel++)
            for (var coefficient = 0; coefficient < 9; coefficient++)
                probe[channel, coefficient] = AmbientProbe[channel * 9 + coefficient];
            RenderSettings.ambientProbe = probe;
        }
        RenderSettings.fog = Fog;
        RenderSettings.fogMode = FogMode;
        RenderSettings.fogColor = FogColor;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
        RenderSettings.skybox = Skybox;
        RenderSettings.defaultReflectionMode = ReflectionMode;
        RenderSettings.customReflectionTexture = Reflection;
        RenderSettings.reflectionIntensity = ReflectionIntensity;
        RenderSettings.reflectionBounces = ReflectionBounces;
        LightmapSettings.lightmapsMode = LightmapsMode;
    }

    private static float[] CaptureProbe()
    {
        var values = new float[27];
        var probe = RenderSettings.ambientProbe;
        for (var channel = 0; channel < 3; channel++)
        for (var coefficient = 0; coefficient < 9; coefficient++)
            values[channel * 9 + coefficient] = probe[channel, coefficient];
        return values;
    }
}
