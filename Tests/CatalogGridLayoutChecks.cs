using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Tests;

internal static class CatalogGridLayoutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(CatalogGridLayout.Capacity(1800, 640) == 60, "Wide catalog fills five compact rows within the thumbnail budget");
        check(CatalogGridLayout.Capacity(450, 426) == 12, "Narrow catalog includes its partially visible fourth row");
        check(CatalogGridLayout.Capacity(1800, 360) == 48, "Wide viewport includes its partially visible fourth row");
        check(CatalogGridLayout.Capacity(900, 360) == 24, "Narrower viewport includes its partially visible fourth row");
        check(CatalogGridLayout.Capacity(450, 235) == 6, "Second row remains available when its bottom is clipped");
        check(CatalogGridLayout.Capacity(450, 236) == 6, "Capacity includes the second row at its exact outer height");
        check(CatalogGridLayout.Capacity(2250, 220) == 30, "Wide shallow scene browser shows a second scrollable row");
        check(CatalogGridLayout.Capacity(450, 118) == 3, "Exact row boundary does not add an invisible row");
        check(CatalogGridLayout.Capacity(450, 119) == 6, "Even a small visible part of the next row is populated");
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
