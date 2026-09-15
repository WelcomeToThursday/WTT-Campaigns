using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Tests;

internal static class ScenePreviewChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var released = new List<string>();
        var cache = new ScenePreviewCache<string>(2, released.Add);
        var generation = cache.Generation;
        check(cache.Request("crate") && !cache.Request("crate"), "Preview requests coalesce while loading");
        cache.Fail(generation, "crate", "Unavailable");
        check(!cache.Request("crate") && cache.Error("crate") == "Unavailable", "Failed preview remains explicit without a retry loop");
        cache.Retry("crate");
        check(cache.Request("crate"), "Unavailable preview can be retried");
        cache.Complete(generation, "crate", "crate texture", "crate");
        cache.Complete(generation, "loot", "native icon lease", "crate");
        cache.Complete(generation, "barrel", "barrel texture", "crate");
        check(
            cache.Get("crate") != null && cache.Get("barrel") != null && cache.Get("loot") == null,
            "Cache eviction retains the selected preview"
        );
        check(released.SequenceEqual(new[] { "native icon lease" }), "Eviction releases exactly one result");
        cache.Request("pending");
        cache.Clear();
        check(
            !cache.Complete(generation, "pending", "late texture", "") && cache.Get("pending") == null,
            "Late preview cannot attach to the next map"
        );
        cache.Fail(generation, "pending", "stale error");
        check(cache.Error("pending") == "" && cache.Request("pending"), "Stale failure cannot poison the next map");
        cache.Abandon("pending");
        check(cache.Request("pending"), "Pruned page request can be queued again");
        check(released.Count == 4 && released.Distinct().Count() == 4, "Clear and stale completion release all owned results once");

        var pages = new ScenePreviewCache<string>(64, _ => { });
        for (var i = 0; i < 64; i++)
            pages.Complete(pages.Generation, "old" + i, "image" + i, "old0");
        for (var page = 0; page < 10; page++)
        {
            for (var row = 0; row < 10; row++)
            {
                var key = page + ":" + row;
                pages.Complete(pages.Generation, key, "image" + key, "old0");
                pages.Fail(pages.Generation, "error" + key, "Unavailable");
            }
            check(pages.Get("old0") != null, "Paging preserves the selected thumbnail");
            for (var row = 0; row < 10; row++)
            {
                var key = page + ":" + row;
                check(pages.Get(key) != null && !pages.Request(key), "Full cache retains every newly rendered page row without retrying");
                check(
                    pages.Error("error" + key) == "Unavailable" && !pages.Request("error" + key),
                    "Full error cache retains the current page failures without retrying"
                );
            }
        }
    }
}
