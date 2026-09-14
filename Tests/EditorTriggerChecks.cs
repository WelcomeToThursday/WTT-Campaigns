using UnityEngine;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorTriggerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var owner = new GameObject().AddComponent<DisablerCullingObject>();
        var renderer = new GameObject().AddComponent<CullingComponent>();
        var inverse = new GameObject().AddComponent<CullingComponent>();
        var enabled = new GameObject().AddComponent<CullingComponent>();
        enabled.Enabled = true;
        var culled = new GameObject { activeSelf = false };
        var untouched = new GameObject { activeSelf = false };
        var hidden = new GameObject { activeSelf = false };
        var editedLater = new GameObject();
        var hiddenObjects = new HashSet<GameObject> { hidden };
        owner._componentsToTurnOff.AddRange(new[] { renderer, enabled });
        owner._compsToTurnOffWhoIgnoreInversedColliders.Add(inverse);
        owner._gameObjectsToTurnOff.AddRange(new[] { culled, hidden, editedLater });
        owner.Workers(Array.Empty<object>().GetEnumerator(), Array.Empty<object>().GetEnumerator());
        using var visibility = new EditorTriggerVisibility(hiddenObjects.Contains);
        visibility.Observe(owner);
        check(
            renderer.Enabled && inverse.Enabled && enabled.Enabled && culled.activeSelf,
            "Trigger visibility reveals main and inverse-independent components plus culled objects"
        );
        check(
            !hidden.activeSelf && !untouched.activeSelf,
            "Trigger visibility preserves authored hiding and does not activate unregistered objects"
        );
        check(owner.Stopped == 2 && !owner.Pending, "Both pending native hide workers are stopped and cleared");
        var shared = new GameObject().AddComponent<DisablerCullingObject>();
        shared._componentsToTurnOff.Add(renderer);
        shared._gameObjectsToTurnOff.Add(culled);
        visibility.Observe(shared);
        var added = new GameObject().AddComponent<CullingComponent>();
        owner._componentsToTurnOff.Add(added);
        visibility.Observe(owner);
        check(
            added.Enabled && visibility.ComponentCount == 4 && visibility.RevealedCount == 4,
            "Later registrations are revealed once without replacing shared target snapshots"
        );
        culled.SetActive(false);
        hiddenObjects.Add(culled);
        editedLater.SetActive(false);
        hiddenObjects.Add(editedLater);
        visibility.Observe(owner);
        check(!culled.activeSelf && !editedLater.activeSelf, "Repeated native requests do not resurrect authored removals");
        visibility.Dispose();
        check(
            !renderer.Enabled && !inverse.Enabled && !added.Enabled && enabled.Enabled,
            "Walkthrough restores original enabled states including targets shared by switches"
        );
        check(
            !culled.activeSelf && !editedLater.activeSelf && !hidden.activeSelf,
            "Teardown preserves removals made before and during editing"
        );
        check(
            owner.Refreshed == 1 && shared.Refreshed == 1 && visibility.SwitchCount == 0,
            "Teardown requests native trigger reevaluation and releases references"
        );
        visibility.Dispose();
        check(owner.Refreshed == 1, "Repeated trigger teardown is harmless");
        visibility.Observe(owner);
        UnityEngine.Object.Destroy(renderer);
        UnityEngine.Object.Destroy(owner.gameObject);
        UnityEngine.Object.Destroy(culled);
        visibility.Dispose();
        check(!inverse.Enabled && enabled.Enabled, "Consecutive sessions and destroyed map targets clean up safely");
    }
}
