using System.IO;
using UnityEngine;

namespace SeasonalPerks.UI.Media;

public static class SeasonLogoArtwork
{
    private static Sprite? _sprite;

    public static Sprite Load()
    {
        if (_sprite)
            return _sprite!;

        // Use the video's first frame so its padding and scale match during preparation.
#if UNITY_EDITOR
        var bytes = File.ReadAllBytes(
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../SeasonalPerks/UI/Resources/Hub/season-1-logo.png"))
        );
#else
        using var stream =
            typeof(SeasonLogoArtwork).Assembly.GetManifestResourceStream("SeasonalPerks.Hub.season-1-logo.png")
            ?? throw new InvalidDataException("Missing season logo artwork.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
#endif
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
            throw new InvalidDataException("Invalid season logo artwork.");
        texture.name = "Season 1 logo first frame";
        _sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f),
            100,
            0,
            SpriteMeshType.FullRect
        );
        _sprite.name = texture.name;
        return _sprite;
    }
}
