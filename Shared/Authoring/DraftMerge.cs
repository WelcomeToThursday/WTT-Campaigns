using Newtonsoft.Json.Linq;

namespace WTT.Campaigns.Shared.Authoring;

// Three-way merge: identity-bearing arrays merge by identity; ordered scalar arrays remain atomic.
public static class DraftMerge
{
    public static JToken? Merge(JToken? baseline, JToken? local, JToken? remote, List<DraftConflict> conflicts, string path = "")
    {
        if (JToken.DeepEquals(local, baseline))
        {
            return remote?.DeepClone();
        }

        if (JToken.DeepEquals(remote, baseline) || JToken.DeepEquals(local, remote))
        {
            return local?.DeepClone();
        }

        if (baseline is JObject b && local is JObject l && remote is JObject r)
        {
            var result = new JObject();
            foreach (
                var key in b.Properties().AsValueEnumerable().Concat(l.Properties()).Concat(r.Properties()).Select(p => p.Name).Distinct()
            )
            {
                var merged = Merge(b[key], l[key], r[key], conflicts, path + "/" + key);
                if (merged != null)
                {
                    result[key] = merged;
                }
            }
            return result;
        }
        if (baseline == null && local is JArray && remote is JArray)
        {
            baseline = new JArray();
        }

        if (baseline is JArray ba && local is JArray la && remote is JArray ra && Keyed(ba) && Keyed(la) && Keyed(ra))
        {
            var result = new JArray();
            var old = ba.AsValueEnumerable().Select(Key).ToArray();
            var lo = la.AsValueEnumerable().Select(Key).Where(id => old.AsValueEnumerable().Contains(id)).ToArray();
            var ro = ra.AsValueEnumerable().Select(Key).Where(id => old.AsValueEnumerable().Contains(id)).ToArray();
            var order = !lo.AsValueEnumerable().SequenceEqual(old.AsValueEnumerable().Where(id => lo.AsValueEnumerable().Contains(id)))
                ? la.AsValueEnumerable().Concat(ra).ToArray()
                : ra.AsValueEnumerable().Concat(la).ToArray();
            if (
                !lo.AsValueEnumerable().SequenceEqual(old.AsValueEnumerable().Where(id => lo.AsValueEnumerable().Contains(id)))
                && !ro.AsValueEnumerable().SequenceEqual(old.AsValueEnumerable().Where(id => ro.AsValueEnumerable().Contains(id)))
                && !lo.SequenceEqual(ro)
            )
            {
                conflicts.Add(
                    new()
                    {
                        Path = path + "/order",
                        Local = la.ToString(),
                        Remote = ra.ToString(),
                    }
                );
            }

            foreach (var id in order.AsValueEnumerable().Concat(ba).Select(Key).Distinct())
            {
                var merged = Merge(
                    ba.AsValueEnumerable().FirstOrDefault(x => Key(x) == id),
                    la.AsValueEnumerable().FirstOrDefault(x => Key(x) == id),
                    ra.AsValueEnumerable().FirstOrDefault(x => Key(x) == id),
                    conflicts,
                    path + "/" + id
                );
                if (merged != null)
                {
                    result.Add(merged);
                }
            }
            return result;
        }
        conflicts.Add(
            new()
            {
                Path = path,
                Local = local?.ToString() ?? "(deleted)",
                Remote = remote?.ToString() ?? "(deleted)",
            }
        );
        return local?.DeepClone();
    }

    private static string Key(JToken token)
    {
        return (string?)(token["Id"] ?? token["id"] ?? token["_id"]) ?? "";
    }

    private static bool Keyed(JArray values)
    {
        return values.AsValueEnumerable().All(v => v is JObject && Key(v).Length > 0)
            && values.AsValueEnumerable().Select(Key).Distinct().Count() == values.Count;
    }
}
