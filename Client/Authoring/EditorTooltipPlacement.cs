namespace WTT.Campaigns.Client.Authoring;

internal static class EditorTooltipPlacement
{
    internal static (float X, float Y) Place(
        float left,
        float right,
        float top,
        float bottom,
        float width,
        float height,
        float screenWidth,
        float screenHeight
    )
    {
        const float margin = 8,
            gap = 6;
        var x = (left + right - width) / 2;
        var y = bottom + gap;
        if (y + height > screenHeight - margin)
            y = top - height - gap;
        return (
            Math.Clamp(x, margin, Math.Max(margin, screenWidth - width - margin)),
            Math.Clamp(y, margin, Math.Max(margin, screenHeight - height - margin))
        );
    }
}
