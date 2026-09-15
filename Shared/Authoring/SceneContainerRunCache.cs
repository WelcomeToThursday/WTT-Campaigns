using Newtonsoft.Json;

namespace WTT.Campaigns.Shared.Authoring;

// A cached response is immutable even if a caller consumes or edits its returned item tree.
public sealed class SceneContainerRunCache
{
    private readonly Dictionary<string, (string Layout, string Json)> _runs = new();
    private readonly int _capacity;

    public SceneContainerRunCache(int capacity = 64) => _capacity = capacity;

    public SceneContainerResponse Get(string run, string layout, Func<SceneContainerResponse> generate)
    {
        if (
            (!Guid.TryParseExact(run, "N", out _) && !WTT.Campaigns.Shared.Seasons.SeasonValidator.IsId(run))
            || !WTT.Campaigns.Shared.Seasons.SeasonValidator.IsId(layout)
        )
            throw new InvalidOperationException("Invalid container run identity.");
        lock (_runs)
        {
            if (_runs.TryGetValue(run, out var old))
            {
                if (old.Layout != layout)
                    throw new InvalidOperationException("Run belongs to another layout.");
                return JsonConvert.DeserializeObject<SceneContainerResponse>(old.Json)!;
            }
            // Never evict a receipt while its session can still retry it: eviction would permit rerolls.
            if (_runs.Count >= _capacity)
                throw new InvalidOperationException("Reconnect the editor session before starting more container rehearsals.");
            var json = JsonConvert.SerializeObject(generate());
            _runs.Add(run, (layout, json));
            return JsonConvert.DeserializeObject<SceneContainerResponse>(json)!;
        }
    }
}
