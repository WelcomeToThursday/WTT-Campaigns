namespace WTT.Campaigns.Client.Authoring.Views;

internal static class CatalogGridLayout
{
    internal const int MaximumItems = 60;
    internal const int TileWidth = 150;
    internal const int TileHeight = 118;
    internal const int TileMargin = 3;
    internal const int ThumbnailHeight = 64;

    internal static int Capacity(float width, float height)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            return 10;
        var columns = Math.Clamp((int)(width / TileWidth), 1, MaximumItems);
        var rows = Math.Clamp((int)(height / TileHeight), 1, MaximumItems);
        return Math.Min(MaximumItems, columns * rows);
    }

    internal static int Repage(int page, int oldSize, int newSize) => page * oldSize / newSize;

    internal static IEnumerable<(int Page, int Skip, int Take)> Requests(int page, int size)
    {
        var start = page * size;
        var end = start + size;
        for (var serverPage = start / 10; serverPage * 10 < end; serverPage++)
        {
            var first = Math.Max(start, serverPage * 10);
            yield return (serverPage, first - serverPage * 10, Math.Min(end, (serverPage + 1) * 10) - first);
        }
    }
}
