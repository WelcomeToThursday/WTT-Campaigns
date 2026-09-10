using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Media;

namespace WTT.Campaigns.UI.Controls;

// Apply the recovered soft title mask without depending on live-only UI shaders.
public sealed class StoryTitleMask : BaseMeshEffect
{
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || graphic is not Image image || !image.sprite)
            return;
        var mask = StoryUiArtwork.Load("journal-title-mask").texture;
        var rect = graphic.rectTransform.rect;
        var texture = image.sprite.texture;
        var uv = image.sprite.textureRect;
        const int columns = 48;
        const int rows = 12;
        mesh.Clear();
        for (var y = 0; y <= rows; y++)
        for (var x = 0; x <= columns; x++)
        {
            var u = (float)x / columns;
            var v = (float)y / rows;
            var color = graphic.color;
            color.a *= mask.GetPixelBilinear(u, v).a;
            mesh.AddVert(
                new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v),
                color,
                new Vector2((uv.x + uv.width * u) / texture.width, (uv.y + uv.height * v) / texture.height)
            );
            if (x == columns || y == rows)
                continue;
            var index = y * (columns + 1) + x;
            mesh.AddTriangle(index, index + columns + 1, index + columns + 2);
            mesh.AddTriangle(index + columns + 2, index + 1, index);
        }
    }
}
