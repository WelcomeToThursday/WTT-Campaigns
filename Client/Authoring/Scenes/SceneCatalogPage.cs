namespace WTT.Campaigns.Client.Authoring.Scenes;

// Seek directly into two sorted catalogs. Work and allocation depend on page size,
// not on the number of assets preceding the page.
internal static class SceneCatalogPage
{
    internal static List<T> Merge<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, int offset, int count, Comparison<T> compare)
    {
        var skip = Math.Min(Math.Max(0, offset), left.Count + right.Count);
        var low = Math.Max(0, skip - right.Count);
        var high = Math.Min(skip, left.Count);
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            var other = skip - middle;
            if (other > 0 && middle < left.Count && compare(left[middle], right[other - 1]) <= 0)
                low = middle + 1;
            else
                high = middle;
        }
        var a = low;
        var b = skip - a;
        var result = new List<T>(Math.Min(Math.Max(0, count), left.Count + right.Count - skip));
        while (result.Count < count && (a < left.Count || b < right.Count))
            result.Add(b >= right.Count || a < left.Count && compare(left[a], right[b]) <= 0 ? left[a++] : right[b++]);
        return result;
    }

    internal static bool Contains<T>(IReadOnlyList<T> entries, T item, Comparison<T> compare)
    {
        var low = 0;
        var high = entries.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var order = compare(entries[middle], item);
            if (order == 0)
                return true;
            if (order < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return false;
    }
}
