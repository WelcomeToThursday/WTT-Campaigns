using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WTT.Campaigns.UI.Media;

// Vendored Material Symbols; no font glyphs or network requests at runtime.
public static class EditorMaterialArtwork
{
    private static readonly Dictionary<string, Sprite> Sprites = new();

    public static Sprite Load(string name)
    {
        if (Sprites.TryGetValue(name, out var sprite) && sprite)
            return sprite;
#if UNITY_EDITOR
        var bytes = File.ReadAllBytes(
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/UI/Resources/Editor", name + ".png"))
        );
#else
        using var stream =
            typeof(EditorMaterialArtwork).Assembly.GetManifestResourceStream("WTT.Campaigns.Editor." + name + ".png")
            ?? throw new InvalidDataException("Missing editor artwork: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
#endif
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes, true))
            throw new InvalidDataException("Invalid editor artwork: " + name);
        texture.name = "Editor Material " + name;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f),
            100,
            0,
            SpriteMeshType.FullRect,
            name == "surface" ? new Vector4(12, 12, 12, 12) : Vector4.zero
        );
        sprite.name = texture.name;
        Sprites[name] = sprite;
        return sprite;
    }
}
