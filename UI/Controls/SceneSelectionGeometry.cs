using UnityEngine;

namespace WTT.Campaigns.UI.Controls;

// Selection is intentionally independent of whether the selected object may be edited.
public static class SceneSelectionGeometry
{
    public static Transform VisualRoot(Transform hit)
    {
        for (var node = hit; node && node.parent; node = node.parent)
        {
            if (node.GetComponent<Renderer>())
            {
                var lod = node.GetComponentInParent<LODGroup>();
                return lod ? lod.transform : node;
            }
            var pending = new System.Collections.Generic.Stack<Transform>();
            pending.Push(node);
            var count = 0;
            var visible = false;
            while (pending.Count > 0 && ++count <= 256)
            {
                var child = pending.Pop();
                visible |= child.GetComponent<Renderer>();
                for (var i = 0; i < child.childCount && pending.Count <= 256; i++) pending.Push(child.GetChild(i));
            }
            if (count > 256 || pending.Count > 0) break;
            if (visible) return node;
        }
        return hit;
    }

    public static void WorldScale(Transform target, Vector3 desired)
    {
        var world = target.lossyScale;
        var local = target.localScale;
        for (var axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(world[axis]) < .00001f) throw new System.InvalidOperationException("The object's parent has zero scale.");
            local[axis] *= desired[axis] / world[axis];
        }
        target.localScale = local;
    }

    // Use Unity's actual posed matrix, including parent shear and native scale decomposition.
    public static Vector3 PositionForAnchor(Transform posedTarget, Vector3 localAnchor, Vector3 anchor) =>
        posedTarget.position + (anchor - posedTarget.TransformPoint(localAnchor));

    public static Quaternion Rotation(Quaternion before, int axis, float degrees) =>
        Quaternion.AngleAxis(degrees, axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward) * before;

    public static float Resize(float original, float pixels, bool snap)
    {
        if (Mathf.Abs(pixels) < .0001f) return original;
        var value = original * Mathf.Pow(2, pixels / 90f);
        if (snap) value = Mathf.Round(value / .05f) * .05f;
        return Mathf.Clamp(value, .05f, 1000);
    }
}
