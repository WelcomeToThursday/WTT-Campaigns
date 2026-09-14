using System;

namespace WTT.Campaigns.UI.Controls;

// Display-independent local preferences. No campaign or profile data belongs here.
[Serializable]
public sealed class EditorWindowLayout
{
    public int Version = 1;
    public EditorWindowPlacement[] Windows = Array.Empty<EditorWindowPlacement>();
}

[Serializable]
public sealed class EditorWindowPlacement
{
    public string Id = "";
    public float X, Y, Width, Height;
    public bool Visible;

    public static EditorWindowPlacement Fit(EditorWindowPlacement source, float screenWidth, float screenHeight, float minWidth, float minHeight, float bottomInset = 40)
    {
        float Limit(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        var width = Limit(Finite(source.Width, minWidth), Math.Min(minWidth, screenWidth - 16), screenWidth - 16);
        var height = Limit(Finite(source.Height, minHeight), Math.Min(minHeight, screenHeight - 92 - bottomInset), screenHeight - 92 - bottomInset);
        return new EditorWindowPlacement
        {
            Id = source.Id, Visible = source.Visible, Width = width, Height = height,
            X = Limit(Finite(source.X, 0) * screenWidth, -screenWidth / 2 + width / 2 + 8, screenWidth / 2 - width / 2 - 8) / screenWidth,
            Y = Limit(Finite(source.Y, 0) * screenHeight, -screenHeight / 2 + height / 2 + bottomInset, screenHeight / 2 - height / 2 - 92) / screenHeight,
        };
    }
}
