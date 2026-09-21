using UnityEngine;
using UnityEngine.UI;

namespace WTT.Campaigns.Client.Missions;

/// <summary>A small vector infinity mark, independent of the native timer font's glyph coverage.</summary>
internal sealed class MissionInfinitySymbol : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        var extent = Mathf.Min(rect.width * .35f, rect.height * .9f);
        var thickness = Mathf.Max(1.5f, extent * .1f);
        Vector2 Point(float angle)
        {
            var sin = Mathf.Sin(angle);
            var cos = Mathf.Cos(angle);
            return rect.center + new Vector2(extent * cos / (1 + sin * sin), extent * 1.5f * sin * cos / (1 + sin * sin));
        }
        for (var i = 0; i < 96; i++)
        {
            var a = Point(i * Mathf.PI * 2 / 96);
            var b = Point((i + 1) * Mathf.PI * 2 / 96);
            var direction = (b - a).normalized;
            var normal = new Vector2(-direction.y, direction.x) * thickness * .5f;
            var index = mesh.currentVertCount;
            mesh.AddVert(a - normal, color, Vector2.zero);
            mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero);
            mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
