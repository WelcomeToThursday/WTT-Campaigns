using System.Reflection;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class LevelPropLibraryChecks
{
    // Exercise the shipped metadata search implementation without creating Unity objects or a plugin.
    internal static void Run(string game, string clientPath)
    {
        var context = new ClientAssemblyContext(game, clientPath);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(clientPath));
        var type = assembly.GetType("WTT.Campaigns.Client.Authoring.Scenes.NativeLevelPropLibrary", throwOnError: true)!;
        var search = type.GetMethod("Search", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, bool, CancellationToken, Task<SceneCatalogEntry[]>>>();
        var catalog = JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(clientPath)!, "LevelPropLibrary", "catalog.json")));
        var entries = (JArray)catalog["Entries"]!;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var all = search("", false, CancellationToken.None).GetAwaiter().GetResult();
        var cold = watch.ElapsedMilliseconds;
        if (all.Length != entries.Count || all.Select(e => e.Id).Distinct().Count() != all.Length)
            throw new InvalidOperationException("The client must expose every unique generated level entry.");
        var available = search("", true, CancellationToken.None).GetAwaiter().GetResult();
        if (available.Length != entries.Count(e => (string?)e["Error"] == "") || available.Any(e => e.Error.Length > 0))
            throw new InvalidOperationException("Hide unavailable must filter the complete level index before pagination.");
        if (available.Any(e => e.AssetTarget is not { Kind: "AssetProp" } target || !SceneAssetRules.Valid(target)))
            throw new InvalidOperationException("Generated level props must retain the existing saved asset contract.");
        for (var i = 1; i < available.Length; i++)
            if (string.Compare(available[i - 1].Name, available[i].Name, StringComparison.OrdinalIgnoreCase) > 0)
                throw new InvalidOperationException("Level query results must arrive sorted before merging with current-map entries.");
        watch.Restart();
        var concrete = search("conc", true, CancellationToken.None).GetAwaiter().GetResult();
        var warm = watch.ElapsedMilliseconds;
        if (concrete.Length == 0 || concrete.Select(e => e.AssetTarget!.Path).Distinct().Count() < 2)
            throw new InvalidOperationException("The reported concrete search must find scenery across multiple source scenes.");
        if (!search("level54", true, CancellationToken.None).GetAwaiter().GetResult().Any(e => e.AssetTarget!.Path.Contains("level54")))
            throw new InvalidOperationException("Level source filenames must be searchable.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            search("", false, cancelled.Token).GetAwaiter().GetResult();
            throw new InvalidOperationException("Cancelled level search returned stale results.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine(
            $"Level catalog runtime checks: {available.Length} available / {all.Length} total; 'conc' finds {concrete.Length} entries across source scenes; cold metadata/search {cold} ms, warm search {warm} ms offline. Cancellation and saved identities passed."
        );
    }
}
