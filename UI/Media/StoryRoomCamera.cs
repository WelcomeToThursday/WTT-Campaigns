using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeasonalPerks.UI.Media;

public static class StoryRoomCamera
{
    public static GameObject InstantiateRoom(GameObject prefab)
    {
        // Skinning and world-space shaders lose precision at the old 20km offset.
        return UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
    }

    public static RenderTexture CreateTexture(int width, int height)
    {
        // EFT's room shaders use alpha as rendering data, not UI opacity.
        // An RGB target prevents uGUI from blending the entire room away.
        var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)
            ? RenderTextureFormat.RGB111110Float
            : RenderTextureFormat.RGB565;
        var texture = new RenderTexture(Math.Max(width, 1), Math.Max(height, 1), 24, format);
        texture.Create();
        return texture;
    }

    public static Camera Prepare(GameObject room, string traderId = "")
    {
        foreach (var node in room.GetComponentsInChildren<Transform>(true))
            node.gameObject.layer = 30;
        foreach (var light in room.GetComponentsInChildren<Light>(true))
            light.cullingMask = 1 << 30;
        // Scene LightProbeGroups are not part of these prefab bundles. Use the
        // recovered room ambient instead of sampling a lobby probe at this position.
        foreach (var renderer in room.GetComponentsInChildren<Renderer>(true))
            renderer.lightProbeUsage = LightProbeUsage.Off;
        var cameras = room.GetComponentsInChildren<Camera>(true);
        var camera =
            cameras.SingleOrDefault(c => c.name == "StoryCamera")
            ?? throw new InvalidOperationException("The trader room requires one StoryCamera.");
        foreach (var other in cameras)
            other.enabled = false;
        // Debug cameras in the recovered rooms are saved as inactive objects.
        // Activate only the camera's ancestors, retaining authored room visibility.
        for (var node = camera.transform; node != room.transform; node = node.parent)
            node.gameObject.SetActive(true);
        camera.cullingMask = 1 << 30;
        camera.renderingPath = RenderingPath.DeferredShading;
        camera.allowHDR = true;
        camera.allowMSAA = false;
        camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
        // These prefabs have no scene occlusion data loaded with them.
        camera.useOcclusionCulling = false;
        camera.rect = new Rect(0, 0, 1, 1);
        var clear = camera.backgroundColor;
        clear.a = 1;
        camera.backgroundColor = clear;
        _ = room.GetComponent<StoryRoomIsolation>() ?? room.AddComponent<StoryRoomIsolation>();
        if (traderId.Length > 0)
            StoryRoomAmbient.Configure(room, camera, traderId);
        return camera;
    }
}
