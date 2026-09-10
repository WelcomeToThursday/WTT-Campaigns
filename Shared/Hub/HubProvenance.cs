using System;
using System.Linq;

namespace WTT.Campaigns.Shared.Hub;

public static class HubProvenance
{
    public static void Register(HubRaid raid, string id, string template, int count, bool spawned)
    {
        if (count < 1 || count > 999 || raid.Stacks.ContainsKey(id))
        {
            throw new InvalidOperationException("Invalid document stack.");
        }
        var stack = new HubDocumentStack { Template = template };
        for (var index = 0; index < count; index++)
        {
            var unit = id + ":" + index;
            stack.Units.Add(unit);
            if (spawned)
            {
                raid.Spawned.Add(unit, template);
            }
        }
        raid.Stacks.Add(id, stack);
    }

    public static void Transfer(HubRaid raid, string source, string target, int count, bool split)
    {
        if (raid.Finished || source == target || count < 1 || !raid.Stacks.TryGetValue(source, out var from) || from.Units.Count < count)
        {
            throw new InvalidOperationException("Invalid document transfer.");
        }
        if (split)
        {
            if (raid.Stacks.ContainsKey(target))
            {
                throw new InvalidOperationException("Document split identifier already exists.");
            }
            raid.Stacks.Add(target, new HubDocumentStack { Template = from.Template });
        }
        if (!raid.Stacks.TryGetValue(target, out var to) || to.Template != from.Template || to.Units.Count + count > 999)
        {
            throw new InvalidOperationException("Invalid destination document stack.");
        }
        // Always move the same conserved units. Reacquisition and subsequent splits cannot mint pickup identities.
        to.Units.AddRange(from.Units.Take(count));
        from.Units.RemoveRange(0, count);
    }
}
