namespace WTT.Campaigns.Client.Authoring.Views;

internal static class EditorActionGridLayout
{
    internal static int Columns(int count, float width, float preferredWidth, float gap)
    {
        if (count <= 0)
            return 0;
        if (!float.IsFinite(width) || width <= 0 || !float.IsFinite(preferredWidth))
            return 1;
        gap = float.IsFinite(gap) ? Math.Max(0, gap) : 0;
        return Math.Min(count, Math.Max(1, (int)Math.Floor((width + gap) / (Math.Max(1, preferredWidth) + gap))));
    }

    internal static float CellWidth(float width, int columns, float gap)
    {
        if (columns <= 0 || !float.IsFinite(width) || width <= 0)
            return 0;
        gap = float.IsFinite(gap) ? Math.Max(0, gap) : 0;
        // Round down to keep Yoga from overflowing a row by a fraction of a pixel.
        return (float)Math.Floor(Math.Max(1, (width - (columns - 1) * gap) / columns) * 64) / 64;
    }
}
