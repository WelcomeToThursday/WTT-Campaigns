using UnityEngine;
using UnityEngine.Rendering;

namespace WTT.Campaigns.UI.Controls;

public static class SceneHandleMath
{
    public static float HitPath(Camera camera, Vector3[] path, Vector2 mouse, out Vector2 tangent)
    {
        var nearest = float.PositiveInfinity;
        tangent = Vector2.right;
        for (var i = 1; i < path.Length; i++)
        {
            var a = camera.WorldToScreenPoint(path[i - 1]);
            var b = camera.WorldToScreenPoint(path[i]);
            if (a.z <= camera.nearClipPlane || b.z <= camera.nearClipPlane) continue;
            var direction = (Vector2)(b - a);
            if (direction.sqrMagnitude < .01f) continue;
            var amount = Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)a, direction) / direction.sqrMagnitude);
            var distance = Vector2.Distance(mouse, (Vector2)a + direction * amount);
            if (distance >= nearest) continue;
            nearest = distance;
            tangent = direction.normalized;
        }
        return nearest;
    }

    public static Material LineMaterial(bool overlay)
    {
        var shader = Shader.Find("Hidden/Internal-Colored");
        if (!shader || !shader.isSupported) throw new System.InvalidOperationException("The editor line shader is unavailable.");
        var material = new Material(shader);
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)(overlay ? CompareFunction.Always : CompareFunction.LessEqual));
        if (overlay) material.renderQueue = 5000;
        return material;
    }

    public static Vector3 PositionAroundAnchor(Vector3 position, Quaternion rotation, Vector3 scale,
        Quaternion nextRotation, Vector3 nextScale, Vector3 anchor)
    {
        var local = Quaternion.Inverse(rotation) * (anchor - position);
        for (var axis = 0; axis < 3; axis++)
            local[axis] = Mathf.Abs(scale[axis]) > .00001f ? local[axis] / scale[axis] : 0;
        return anchor - nextRotation * Vector3.Scale(nextScale, local);
    }

    public static float MetresPerPixel(Camera camera, Vector3 position)
    {
        var depth = camera.WorldToScreenPoint(position).z;
        return camera.orthographic ? camera.orthographicSize * 2 / Mathf.Max(1, camera.pixelHeight)
            : 2 * Mathf.Max(camera.nearClipPlane, depth) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2) / Mathf.Max(1, camera.pixelHeight);
    }
}
