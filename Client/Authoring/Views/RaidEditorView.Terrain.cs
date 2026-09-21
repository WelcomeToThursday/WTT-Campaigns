using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.Client.Authoring.TerrainEditing;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    private readonly List<Texture2D> _terrainThumbnails = new();

    internal void ClearTerrainPalette()
    {
        foreach (var image in _terrainThumbnails)
            if (image)
                UnityEngine.Object.Destroy(image);
        _terrainThumbnails.Clear();
        Element("TerrainPalette").Clear();
    }

    internal void TerrainPalette(TerrainTile? tile, bool grass, int selected, Action<int> choose)
    {
        ClearTerrainPalette();
        if (tile == null)
            return;
        var names = grass ? tile.Grass : tile.Textures;
        var parent = Element("TerrainPalette");
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var button = Document.Clone<Button>("TerrainPaletteItem");
            button.Q<Label>("Caption").text = names[i];
            button.tooltip = names[i];
            var thumbnail = tile.Thumbnail(grass, i);
            if (
                !thumbnail
                && !grass
                && tile.Surface.materialTemplate
                && tile.Surface.materialTemplate.HasProperty("_Diffuse")
                && tile.Surface.materialTemplate.GetTexture("_Diffuse") is Texture2DArray array
                && i < array.depth
            )
            {
                var mip = Math.Min(
                    array.mipmapCount - 1,
                    Math.Max(0, (int)Math.Ceiling(Math.Log(Math.Max(array.width, array.height) / 128d, 2)))
                );
                var slice = new Texture2D(Math.Max(1, array.width >> mip), Math.Max(1, array.height >> mip), array.format, false)
                {
                    name = "Terrain palette " + i,
                };
                try
                {
                    Graphics.CopyTexture(array, i, mip, slice, 0, 0);
                    _terrainThumbnails.Add(slice);
                    thumbnail = slice;
                }
                catch
                {
                    UnityEngine.Object.Destroy(slice);
                }
            }
            button.Q<Image>("Thumbnail").image = thumbnail;
            button.Q<Image>("Thumbnail").scaleMode = ScaleMode.ScaleToFit;
            button.EnableInClassList("editor-selected", i == selected);
            button.clicked += () =>
            {
                foreach (var child in parent.Children())
                    child.EnableInClassList("editor-selected", ReferenceEquals(child, button));
                choose(index);
            };
            parent.Add(button);
        }
    }
}
