using System;
using System.Linq;

namespace WTT.Campaigns.UI.Controls;

// Display-independent local preferences. No campaign or profile data belongs here.
[Serializable]
public sealed class EditorWindowLayout
{
    public int Version = 2;
    public EditorWindowPlacement[] Windows = Array.Empty<EditorWindowPlacement>();
    public EditorDockNode? Dock;

    public static EditorWindowLayout? Restore(EditorWindowLayout? saved, EditorWindowLayout defaults)
    {
        if (saved?.Windows == null || saved.Version is not (1 or 2))
            return null;
        var windows = defaults.Windows.ToDictionary(p => p.Id, p => p);
        if (saved.Version == 2 && (saved.Dock == null || !EditorDockLayout.Valid(saved.Dock, windows.Keys.ToHashSet())))
            return null;
        var dock = saved.Version == 1 ? EditorDockNode.Default() : saved.Dock!;
        foreach (var source in saved.Windows)
        {
            if (source == null)
                continue;
            var id = source.Id == "Library" ? "Tool:Layouts" : source.Id;
            if (id == null || !windows.ContainsKey(id))
                continue;
            windows[id] = new EditorWindowPlacement
            {
                Id = id,
                X = source.X,
                Y = source.Y,
                Width = source.Width,
                Height = source.Height,
                Visible = source.Visible,
                ManualSize = saved.Version == 1 || source.ManualSize,
                Opened = saved.Version == 1 || source.Opened,
            };
            if (saved.Version == 1)
                dock = EditorDockLayout.Remove(dock, id)!;
        }
        return new() { Windows = windows.Values.ToArray(), Dock = dock };
    }
}

[Serializable]
public sealed class EditorWindowPlacement
{
    public string Id = "";
    public float X,
        Y,
        Width,
        Height;
    public bool Visible;
    public bool ManualSize;
    public bool Opened;

    public static EditorWindowPlacement Fit(
        EditorWindowPlacement source,
        float screenWidth,
        float screenHeight,
        float minWidth,
        float minHeight,
        float bottomInset = 40
    )
    {
        float Limit(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        var width = Limit(Finite(source.Width, minWidth), Math.Min(minWidth, screenWidth - 16), screenWidth - 16);
        var height = Limit(
            Finite(source.Height, minHeight),
            Math.Min(minHeight, screenHeight - 92 - bottomInset),
            screenHeight - 92 - bottomInset
        );
        return new EditorWindowPlacement
        {
            Id = source.Id,
            Visible = source.Visible,
            ManualSize = source.ManualSize,
            Opened = source.Opened,
            Width = width,
            Height = height,
            X = Limit(Finite(source.X, 0) * screenWidth, -screenWidth / 2 + width / 2 + 8, screenWidth / 2 - width / 2 - 8) / screenWidth,
            Y =
                Limit(Finite(source.Y, 0) * screenHeight, -screenHeight / 2 + height / 2 + bottomInset, screenHeight / 2 - height / 2 - 92)
                / screenHeight,
        };
    }
}
