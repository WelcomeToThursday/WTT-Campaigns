using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Tests;

internal static class EditorActionGridChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(EditorActionGridLayout.Columns(3, 340, 100, 4) == 3, "Route markers use three columns when their labels fit");
        check(EditorActionGridLayout.Columns(7, 400, 95, 4) == 4, "Navigation filters fill available columns before adding rows");
        check(EditorActionGridLayout.Columns(7, 280, 95, 4) == 2, "Narrow tools retain pairs instead of prematurely stacking");
        check(EditorActionGridLayout.Columns(2, 204, 88, 4) == 2, "Compact menu retains both Smaller and Larger columns");
        check(EditorActionGridLayout.Columns(3, 280, 180, 4) == 1, "Long labels receive full-width rows before wrapping");
        check(EditorActionGridLayout.Columns(1, 600, 88, 4) == 1, "Single commands fill the row");
        check(EditorActionGridLayout.Columns(0, 600, 88, 4) == 0, "Hidden groups have no columns");
        check(EditorActionGridLayout.Columns(3, float.NaN, 88, 4) == 1, "Unresolved geometry has a stable fallback");
        foreach (var width in new[] { 176f, 180f, 220f, 280f, 340f, 400f, 701.25f })
        for (var count = 1; count <= 8; count++)
        {
            var columns = EditorActionGridLayout.Columns(count, width, 88, 4);
            var cell = EditorActionGridLayout.CellWidth(width, columns, 4);
            var used = columns * cell + (columns - 1) * 4;
            check(used <= width && width - used < .15f, "Equal columns fill the row without fractional overflow");
            check(columns == count || (columns + 1) * 88 + columns * 4 > width, "Column count is maximal for the available width");
        }
    }
}
