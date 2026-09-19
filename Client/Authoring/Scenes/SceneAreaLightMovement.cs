using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class SceneAreaLightMovement
{
    private static readonly System.Reflection.FieldInfo Cached = AccessTools.Field(typeof(AreaLight), "bool_4");
    private static readonly System.Reflection.FieldInfo Initialized = AccessTools.Field(typeof(AreaLight), "bool_0");
    private static readonly System.Reflection.FieldInfo ShadowPlanes = AccessTools.Field(typeof(AreaLight), "_shadowCubePlanes");
    private static readonly System.Reflection.FieldInfo InvertedPlanes = AccessTools.Field(typeof(AreaLight), "_invertedShadowCubePlanes");

    internal static void Refresh(AreaLight[] lights)
    {
        foreach (var light in lights)
        {
            if (!light)
                continue;
            Cached.SetValue(light, false);
            if (Initialized.GetValue(light) is not true)
                continue;
            light.CalculateDefaultValues();
            light.FillShadowCubePlanes(light.ShadowCube, (Vector4[])ShadowPlanes.GetValue(light));
            light.FillShadowCubePlanes(light.InvertedShadowCube, (Vector4[])InvertedPlanes.GetValue(light));
        }
    }
}
