using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Tests;

internal static class CatalogGridLayoutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(CatalogGridLayout.Capacity(1800, 640) == 48, "Wide catalog fills four rows instead of stopping after ten tiles");
        check(CatalogGridLayout.Capacity(450, 426) == 9, "Narrow catalog page fits three columns and rows");
        check(CatalogGridLayout.Capacity(100, 100) == 1, "Small catalog keeps a usable single tile");
        check(CatalogGridLayout.Capacity(4000, 2000) == 60, "Grid remains within the thumbnail cache and row pool budget");
        check(CatalogGridLayout.Capacity(float.NaN, 400) == 10, "Unresolved layout has a stable fallback");
        check(CatalogGridLayout.Repage(3, 48, 10) == 14, "Changing presentation keeps the first visible item on the new page");
        for (var size = 1; size <= 60; size++)
        for (var page = 0; page < 12; page++)
        {
            var start = page * size;
            var indexes = CatalogGridLayout
                .Requests(page, size)
                .SelectMany(r => Enumerable.Range(r.Page * 10, 10).Skip(r.Skip).Take(r.Take))
                .ToArray();
            check(
                indexes.SequenceEqual(Enumerable.Range(start, size)),
                "Catalog server batches cover each grid page without gaps or duplicates"
            );
        }
    }
}
