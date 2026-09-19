using Newtonsoft.Json.Linq;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Tests;

internal static class CheckpointObjectStateChecks
{
    private sealed class Node
    {
        internal int Value;
        internal Node? Next;
        internal readonly List<Node> Children = new();
        internal Dictionary<string, Node> Map = new();
        internal HashSet<Node> Set = new();
        internal Node[] Array = [];
        internal int[] Numbers = [];
        internal System.Collections.ObjectModel.ReadOnlyCollection<Node>? ReadOnly;
        internal System.Collections.ObjectModel.ReadOnlyDictionary<string, Node>? ReadOnlyMap;
        internal Wrapped Wrapper;
        internal Action? Changed;
        internal Action? InitiallyEmpty;

        internal void Count() => Value++;
    }

    private struct Wrapped
    {
        internal List<Node> Items;
    }

    private sealed class External
    {
        internal int Calls;

        internal void Count() => Calls++;
    }

    internal static void Run(Action<bool, string> check)
    {
        var json = JObject.Parse("{\"effect\":{\"value\":7},\"items\":[1,2]}");
        var originalJson = json.DeepClone();
        var effect = json.Property("effect")!;
        var valueToken = (JValue)json["effect"]!["value"]!;
        var items = (JArray)json["items"]!;
        var jsonSnapshot = new CheckpointObjectState([json], o => o is System.Collections.IList);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            valueToken.Value = 99;
            items.Clear();
            items.Add("abandoned");
            effect.Remove();
            var abandoned = new JObject(effect);
            json["extra"] = true;
            jsonSnapshot.Restore();
            check(
                JToken.DeepEquals(json, originalJson),
                "Checkpoint restores JSON properties and nested values repeatedly without IList.Clear"
            );
            check(
                ReferenceEquals(json.Property("effect"), effect)
                    && ReferenceEquals(json["effect"]!["value"], valueToken)
                    && ReferenceEquals(json["items"], items)
                    && effect.Parent == json
                    && abandoned.Count == 0,
                "JSON restoration retains token identity and repairs moved parent links without cloning"
            );
            check(
                effect.Next == json.Property("items") && json.Property("items")!.Previous == effect,
                "JSON restoration preserves property order and sibling links"
            );
        }
        var node = new Node { Value = 4, Numbers = [1, 2] };
        var child = new Node { Value = 9, Next = node };
        node.Next = child;
        node.Children.Add(child);
        node.Map.Add("child", child);
        node.Set.Add(child);
        node.ReadOnly = node.Children.AsReadOnly();
        node.ReadOnlyMap = new(node.Map);
        node.Array = [child];
        node.Wrapper = new() { Items = [child] };
        var external = new External();
        node.Changed = node.Count;
        node.Changed += external.Count;
        bool Owns(object value) => value is Node or System.Collections.IList or System.Collections.IDictionary or HashSet<Node>;
        var snapshot = new CheckpointObjectState([node], Owns);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            node.Value = 100;
            child.Value = 200;
            node.Next = null;
            child.Next = null;
            node.Children.Clear();
            node.Map.Clear();
            node.Set.Clear();
            node.Array[0] = node;
            node.Numbers[0] = 100;
            node.Wrapper.Items.Clear();
            node.Changed -= external.Count; // Retired external subscriptions must stay retired.
            node.InitiallyEmpty = child.Count;
            node.InitiallyEmpty += external.Count;
            snapshot.Restore();
            check(
                node.Value == 4 && child.Value == 9 && ReferenceEquals(node.Next, child) && ReferenceEquals(child.Next, node),
                "Checkpoint graph restores cycles and counters repeatedly"
            );
            check(
                node.Children.Single() == child
                    && node.Map["child"] == child
                    && node.Set.SetEquals([child])
                    && node.Array.Single() == child
                    && node.Numbers.SequenceEqual([1, 2])
                    && node.Wrapper.Items.Single() == child,
                "Checkpoint restores readonly collections, dictionaries, sets, typed arrays and references inside structs"
            );
            check(
                node.ReadOnly.Single() == child && node.ReadOnlyMap["child"] == child,
                "Native read-only views retain their original restored backing collections"
            );
            node.Changed!();
            check(
                node.Value == 5 && external.Calls == attempt,
                "Checkpoint restores owned callbacks without resurrecting retired external observers"
            );
            node.InitiallyEmpty!();
            check(
                child.Value == 9 && external.Calls == attempt + 1,
                "Empty checkpoint event drops abandoned owned callbacks and preserves external observers"
            );
        }
        var replacement = new Node();
        snapshot.Restore([new(node, replacement)]);
        check(
            replacement.Value == 4 && replacement.Next == child && child.Next == replacement,
            "Fresh native actor replacement remaps references throughout checkpoint graph"
        );
        replacement.Changed!();
        check(replacement.Value == 5 && node.Value == 5, "Fresh actor callbacks bind to replacement actor");
        var freshController = new Node();
        snapshot.Restore([new(node, node), new(child, freshController)]);
        check(
            node.Next == freshController && freshController.Value == 9 && freshController.Next == node,
            "Recycled actor object can restore into independently replaced native controllers"
        );
        try
        {
            snapshot.Restore([new(node, new object())]);
            check(false, "Invalid native replacement rejected");
        }
        catch (InvalidOperationException)
        {
            check(true, "Invalid native replacement rejected");
        }

        var run = new MissionRun
        {
            RunId = "r",
            RaidId = "raid",
            CharacterId = "p",
            ContentHash = "hash",
            Status = MissionRunStatuses.Active,
        };
        var pending = new MissionRun
        {
            RunId = "r",
            RaidId = "raid",
            CharacterId = "p",
            ContentHash = "hash",
            AttemptGeneration = 2,
            Restoring = true,
            Status = MissionRunStatuses.Active,
        };
        MissionAcknowledgement.RequireRestore(run, pending, true, true);
        var ended = new MissionRun
        {
            RunId = run.RunId,
            RaidId = run.RaidId,
            CharacterId = run.CharacterId,
            ContentHash = run.ContentHash,
            Status = MissionRunStatuses.Succeeded,
        };
        try
        {
            MissionAcknowledgement.Require(run, ended, true);
            check(false, "Ended test cannot acknowledge defeat as active progress");
        }
        catch (InvalidOperationException)
        {
            check(true, "Ended test cannot acknowledge defeat as active progress");
        }
        try
        {
            MissionAcknowledgement.RequireRestore(run, ended, true, true);
            check(false, "Ended test cannot acknowledge retry preparation");
        }
        catch (InvalidOperationException error)
        {
            check(
                error.Message.Contains("Succeeded") && error.Message.Contains("attempt 2"),
                "Retry rejection includes the expected generation and returned terminal status"
            );
        }
        pending.CharacterId = "foreign";
        try
        {
            MissionAcknowledgement.RequireRestore(run, pending, true, true);
            check(false, "Restore rejects foreign character");
        }
        catch (InvalidOperationException)
        {
            check(true, "Restore rejects foreign character");
        }
        pending.CharacterId = "p";
        try
        {
            MissionAcknowledgement.RequireRestore(run, pending, false, true);
            check(false, "Restore rejects uncommitted response");
        }
        catch (InvalidOperationException)
        {
            check(true, "Restore rejects uncommitted response");
        }
    }
}
