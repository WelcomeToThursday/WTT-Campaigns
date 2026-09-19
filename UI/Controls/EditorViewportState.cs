using System;

namespace WTT.Campaigns.UI.Controls;

[Flags]
public enum EditorOverlays
{
    Zones = 1,
    Routes = 2,
    Ai = 4,
    Bounds = 8,
    Handles = 16,
    All = Zones | Routes | Ai | Bounds | Handles,
}

public sealed class EditorViewportState
{
    public const float ToolbarHeight = 30;
    public EditorOverlays Overlays = EditorOverlays.All;
    public bool Clean;

    public bool Shows(EditorOverlays overlay) => !Clean && (Overlays & overlay) != 0;

    public static EditorDockRect Content(EditorDockRect dock) =>
        new(dock.X, dock.Y + ToolbarHeight, dock.Width, Math.Max(1, dock.Height - ToolbarHeight));
}
