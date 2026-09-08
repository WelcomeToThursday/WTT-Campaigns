using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SeasonalPerks.UI.Media;

public static class StoryUiArtwork
{
    private static readonly Dictionary<string, Sprite> Sprites = new();

    public static Sprite Load(string name) => Load(name, Vector4.zero);

    public static Sprite Load(string name, Vector4 border)
    {
        if (Sprites.TryGetValue(name, out var sprite) && sprite)
            return sprite;
#if UNITY_EDITOR
        var bytes = File.ReadAllBytes(
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/UI/Resources/Story", name + ".png"))
        );
#else
        using var stream =
            typeof(StoryUiArtwork).Assembly.GetManifestResourceStream("SeasonalPerks.Story." + name + ".png")
            ?? throw new InvalidDataException("Missing story artwork: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
#endif
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
            throw new InvalidDataException("Invalid story artwork: " + name);
        texture.name = "Story " + name;
        sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f),
            100,
            0,
            SpriteMeshType.FullRect,
            border
        );
        sprite.name = texture.name;
        Sprites[name] = sprite;
        return sprite;
    }
}
