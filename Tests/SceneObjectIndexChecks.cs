using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Tests;

internal static class SceneObjectIndexChecks
{
    private sealed class Node(string path)
    {
        internal readonly string Path = path;
        internal bool Alive = true;
    }

    internal static void Run(Action<bool, string> check)
    {
        var calls = 0;
        var index = new SceneObjectIndex<Node>(
            n =>
            {
                calls++;
                return n.Path;
            },
            n => n.Alive
        );
        Node? picked = null;
        for (var i = 0; i < 50000; i++)
        {
            var name = "prop_" + i;
            var node = new Node("Customs:/Warehouse/" + name);
            index.Add(node, name);
            if (i == 12345)
                picked = node;
        }
        index.Complete = true;
        var rows = index.Search("");
        check(rows.Count == 50000 && calls == 0, "Opening Scene does not construct 50,000 object paths");
        check(index.Unique(picked!.Path) == picked && calls == 1, "Picking resolves only matching leaf candidates, not the entire map");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var unchanged = true;
        for (var i = 0; i < 2000; i++)
            unchanged &=
                ReferenceEquals(rows, index.Search("")) && index.Unique(picked.Path) == picked && index.Path(picked) == picked.Path;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(unchanged && calls == 1 && bytes < 1024, "2,000 unchanged Scene/pick refreshes allocate under 1 KiB and build no more paths");
        var named = index.Search("prop_");
        check(named.Count == 50000 && calls == 1, "Name searches do not materialize hierarchy paths");
        var paths = index.Search("Warehouse");
        var pathCalls = calls;
        check(paths.Count == 50000, "Explicit hierarchy searches retain all matching records");
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++)
            unchanged &= ReferenceEquals(paths, index.Search("Warehouse"));
        check(
            unchanged && calls == pathCalls && GC.GetAllocatedBytesForCurrentThread() - before < 1024,
            "Repeated hierarchy searches reuse the bounded result/path cache"
        );
        picked.Alive = false;
        check(index.Unique(picked.Path) == null, "Destroyed picked objects cannot resolve from the cache");
        var same = new SceneObjectIndex<Node>(n => n.Path, n => n.Alive);
        same.Add(new Node("Customs:/A/door"), "door");
        same.Add(new Node("Customs:/B/door"), "door");
        var otherScene = new Node("Woods:/A/door");
        same.Add(otherScene, "door");
        same.Complete = true;
        check(
            same.Unique(otherScene.Path) == otherScene && same.Unique("Customs:/A/door") != null,
            "Matching leaf names preserve exact hierarchy and scene identity"
        );
        same.Add(new Node("Customs:/A/door"), "door");
        check(same.Unique("Customs:/A/door") == null, "Duplicate exact paths remain ambiguous after cache invalidation");
        same.Complete = false;
        check(same.Unique(otherScene.Path) == null, "Partial scans never authorize a unique target");
        same.Clear();
        check(
            same.Count == 0 && same.RetainedCharacters == 0 && !same.Complete && !same.Limited,
            "Raid cleanup releases cached scene objects and path references"
        );
        var budget = new SceneObjectIndex<Node>(n => new string('x', SceneObjectIndex<Node>.MaxPathLength), n => true);
        for (var i = 0; i < 3000 && !budget.Limited; i++)
        {
            var node = new Node("");
            budget.Add(node, "n" + i);
            budget.Path(node);
        }
        check(
            budget.Limited && budget.RetainedCharacters <= SceneObjectIndex<Node>.MaxCharacters,
            "Path cache stops within its 8M character budget"
        );
        var entries = new SceneObjectIndex<Node>(n => n.Path, n => true);
        for (var i = 0; i < SceneObjectIndex<Node>.MaxEntries + 5; i++)
            entries.Add(new Node(""), "same");
        check(entries.Count == SceneObjectIndex<Node>.MaxEntries && entries.Limited, "Node cap bounds giant scene indexes");
        var deep = new SceneObjectIndex<Node>(n => n.Path, n => true);
        var oversized = new Node(new string('x', SceneObjectIndex<Node>.MaxPathLength + 1));
        deep.Add(oversized, "large");
        check(deep.Path(oversized) == null && deep.Limited, "Oversized scene paths are rejected instead of retained");
        Console.WriteLine(
            $"Scene lookup stress: 50,000 objects; 2,000 cached refreshes allocated {bytes} bytes; one target path built. Bounds and ambiguity checks passed."
        );
    }
}
