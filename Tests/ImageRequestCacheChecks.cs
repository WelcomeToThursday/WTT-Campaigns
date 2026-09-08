using SeasonalPerks.Client.UI;

namespace SeasonalPerks.Tests;

internal static class ImageRequestCacheChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var cache = new ImageRequestCache();
        var response = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        Task<byte[]> Download()
        {
            Interlocked.Increment(ref requests);
            return response.Task;
        }
        var consumers = Enumerable.Range(0, 32).Select(_ => cache.GetAsync("server/hub/image?season=a&revision=1", Download)).ToArray();
        check(requests == 1, "Concurrent image consumers share one server request");
        var bytes = new byte[] { 1, 2, 3 };
        response.SetResult(bytes);
        await Task.WhenAll(consumers).WaitAsync(TimeSpan.FromSeconds(5));
        check(consumers.All(t => ReferenceEquals(t.Result, bytes)), "Shared image request serves every consumer");
        await cache.GetAsync("server/hub/image?season=a&revision=1", Download);
        check(requests == 1, "Reopening a screen uses cached image bytes");
        foreach (
            var key in new[]
            {
                "server/hub/image?season=a&revision=2",
                "server/hub/image?season=b&revision=1",
                "other-server/hub/image?season=a&revision=1",
                "server/icons/image?season=a&revision=1",
            }
        )
        {
            await cache.GetAsync(key, Download);
        }
        check(requests == 5, "Server, route, season and revision isolate image cache entries");

        var failed = cache.GetAsync("failed", () => throw new IOException("offline"));
        try
        {
            await failed;
            check(false, "Failed download should throw");
        }
        catch (IOException) { }
        check(ReferenceEquals(await cache.GetAsync("failed", Download), bytes), "Failed downloads can be retried");
        foreach (var empty in new[] { Array.Empty<byte>(), null! })
        {
            try
            {
                await cache.GetAsync("empty", () => Task.FromResult(empty));
                check(false, "Empty image response should throw");
            }
            catch (InvalidDataException) { }
        }
        check(ReferenceEquals(await cache.GetAsync("empty", Download), bytes), "Empty responses never poison the cache");

        cache.Reject("failed", bytes);
        var replacement = new byte[] { 4 };
        await cache.GetAsync("failed", () => Task.FromResult(replacement));
        cache.Reject("failed", bytes);
        check(
            ReferenceEquals(await cache.GetAsync("failed", Download), replacement),
            "Invalid image is evicted without rejecting its replacement"
        );

        var bounded = new ImageRequestCache(maxBytes: 6, maxEntries: 2);
        var boundedRequests = new List<string>();
        Task<byte[]> Get(string key, int size = 3)
        {
            return bounded.GetAsync(
                key,
                () =>
                {
                    boundedRequests.Add(key);
                    return Task.FromResult(new byte[size]);
                }
            );
        }
        await Get("a");
        await Get("b");
        await Get("a");
        await Get("c");
        await Get("a");
        await Get("b");
        check(boundedRequests.SequenceEqual(new[] { "a", "b", "c", "b" }), "Cache evicts the least recently used image");
        await Get("large", 7);
        await Get("large", 7);
        check(boundedRequests.Count(k => k == "large") == 2, "Oversized images are served without retaining them");
        await Get("full", 6);
        var before = boundedRequests.Count;
        await Get("a");
        check(boundedRequests.Count == before + 1, "Byte budget evicts images even below the entry limit");
        var countBounded = new ImageRequestCache(maxBytes: 100, maxEntries: 1);
        await countBounded.GetAsync("a", () => Task.FromResult(bytes));
        await countBounded.GetAsync("b", () => Task.FromResult(bytes));
        var reloaded = false;
        await countBounded.GetAsync(
            "a",
            () =>
            {
                reloaded = true;
                return Task.FromResult(bytes);
            }
        );
        check(reloaded, "Entry count is bounded independently of byte size");

        var throttled = new ImageRequestCache(concurrentRequests: 1);
        var first = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = throttled.GetAsync("first", () => first.Task);
        var secondStarted = false;
        var secondTask = throttled.GetAsync(
            "second",
            () =>
            {
                secondStarted = true;
                return Task.FromResult(bytes);
            }
        );
        check(!secondStarted, "Distinct image downloads respect the concurrency limit");
        first.SetException(new IOException("offline"));
        try
        {
            await firstTask;
        }
        catch (IOException) { }
        await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        check(secondStarted, "A failed download releases its request slot");
    }
}
