using System;

namespace WTT.Campaigns.UI.Controls;

// Dock coordinates start at the top left; Unity screen coordinates start at the bottom left.
public static class EditorViewportCoordinates
{
    public static EditorDockRect Pixels(EditorDockRect dock, float scale, int width, int height)
    {
        var left = Math.Clamp((int)Math.Round(dock.X * scale), 0, Math.Max(0, width - 1));
        var top = Math.Clamp((int)Math.Round(dock.Y * scale), 0, Math.Max(0, height - 1));
        var right = Math.Clamp((int)Math.Round((dock.X + dock.Width) * scale), left + 1, Math.Max(1, width));
        var bottom = Math.Clamp((int)Math.Round((dock.Y + dock.Height) * scale), top + 1, Math.Max(1, height));
        return new(left, height - bottom, right - left, bottom - top);
    }

    public static (float X, float Y) Normalize(EditorDockRect pixels, float x, float y) =>
        ((x - pixels.X) / Math.Max(1, pixels.Width), (y - pixels.Y) / Math.Max(1, pixels.Height));

    public static (float X, float Y) Project(EditorDockRect pixels, float x, float y) =>
        (pixels.X + x * pixels.Width, pixels.Y + y * pixels.Height);
}
