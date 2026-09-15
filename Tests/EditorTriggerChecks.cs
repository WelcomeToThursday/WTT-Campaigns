using UnityEngine;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorTriggerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static void Drain(EditorTriggerVisibility visibility)
        {
            for (var attempt = 0; visibility.Pending && attempt < 1000; attempt++)
                visibility.Advance(4096, double.PositiveInfinity);
            if (visibility.Pending)
                throw new InvalidOperationException("Trigger visibility did not drain in the test budget.");
        }

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
            !renderer.Enabled && !inverse.Enabled && !culled.activeSelf && visibility.Pending,
            "Trigger visibility queues target work without blocking observation"
        );
        Drain(visibility);
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
        Drain(visibility);
        check(
            added.Enabled && visibility.ComponentCount == 4 && visibility.RevealedCount == 4,
            "Later registrations are revealed once without replacing shared target snapshots"
        );
        culled.SetActive(false);
        hiddenObjects.Add(culled);
        editedLater.SetActive(false);
        hiddenObjects.Add(editedLater);
        visibility.Observe(owner);
        Drain(visibility);
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
        using var consecutive = new EditorTriggerVisibility(hiddenObjects.Contains);
        consecutive.Observe(owner);
        Drain(consecutive);
        UnityEngine.Object.Destroy(renderer);
        UnityEngine.Object.Destroy(owner.gameObject);
        UnityEngine.Object.Destroy(culled);
        consecutive.Dispose();
        check(!inverse.Enabled && enabled.Enabled, "Consecutive sessions and destroyed map targets clean up safely");

        var partialOwner = new GameObject().AddComponent<DisablerCullingObject>();
        var firstPartial = new GameObject().AddComponent<CullingComponent>();
        var secondPartial = new GameObject().AddComponent<CullingComponent>();
        partialOwner._componentsToTurnOff.AddRange(new[] { firstPartial, secondPartial });
        using var partial = new EditorTriggerVisibility(_ => false);
        partial.Observe(partialOwner);
        partial.Advance(1, double.PositiveInfinity);
        check(
            partial.Pending && !firstPartial.Enabled && !secondPartial.Enabled,
            "A bounded advance charges the owner transition before target work"
        );
        partial.Observe(partialOwner);
        partial.Advance(1, double.PositiveInfinity);
        check(
            partial.Pending && firstPartial.Enabled && !secondPartial.Enabled,
            "Reobserving a partially processed owner preserves its cursor"
        );
        partial.Dispose();
        check(!firstPartial.Enabled && !secondPartial.Enabled, "Partial teardown restores only targets whose state was captured");
        secondPartial.Enabled = true;
        partial.Advance(4096, double.PositiveInfinity);
        check(secondPartial.Enabled, "Disposed pending work cannot mutate targets later");

        var boundedOwner = new GameObject().AddComponent<DisablerCullingObject>();
        var firstBounded = new GameObject().AddComponent<CullingComponent>();
        var secondBounded = new GameObject().AddComponent<CullingComponent>();
        boundedOwner._componentsToTurnOff.AddRange(new[] { firstBounded, secondBounded });
        using (var bounded = new EditorTriggerVisibility(_ => false))
        {
            bounded.Observe(boundedOwner);
            bounded.Advance(1, double.PositiveInfinity);
            bounded.Advance(1, double.PositiveInfinity);
            check(firstBounded.Enabled && !secondBounded.Enabled, "A bounded advance processes one target at a time");
            bounded.Advance(1, double.PositiveInfinity);
            check(!bounded.Pending && secondBounded.Enabled, "Bounded advances eventually complete an owner");
        }

        var dynamicOwner = new GameObject().AddComponent<DisablerCullingObject>();
        var dynamicFirst = new GameObject().AddComponent<CullingComponent>();
        var dynamicLater = new GameObject().AddComponent<CullingComponent>();
        dynamicOwner._componentsToTurnOff.Add(dynamicFirst);
        using (var dynamic = new EditorTriggerVisibility(_ => false))
        {
            dynamic.Observe(dynamicOwner);
            dynamicOwner._componentsToTurnOff.Add(dynamicLater);
            Drain(dynamic);
            check(dynamicFirst.Enabled && dynamicLater.Enabled, "Queued target cursors include list entries appended before processing");
        }

        var rescanOwner = new GameObject().AddComponent<DisablerCullingObject>();
        var rescanFirst = new GameObject().AddComponent<CullingComponent>();
        var rescanInverse = new GameObject().AddComponent<CullingComponent>();
        var rescanEarlier = new GameObject().AddComponent<CullingComponent>();
        rescanOwner._componentsToTurnOff.Add(rescanFirst);
        rescanOwner._compsToTurnOffWhoIgnoreInversedColliders.Add(rescanInverse);
        using (var rescan = new EditorTriggerVisibility(_ => false))
        {
            rescan.Observe(rescanOwner);
            rescan.Advance(2, double.PositiveInfinity);
            rescanOwner._componentsToTurnOff.Add(rescanEarlier);
            rescan.Observe(rescanOwner);
            Drain(rescan);
            check(
                rescanFirst.Enabled && rescanEarlier.Enabled && rescanInverse.Enabled,
                "Reobserving after a cursor passed a list rescans newly appended targets"
            );
        }

        var sharedOwner = new GameObject().AddComponent<DisablerCullingObject>();
        var secondSharedOwner = new GameObject().AddComponent<DisablerCullingObject>();
        var sharedTarget = new GameObject().AddComponent<CullingComponent>();
        sharedOwner._componentsToTurnOff.Add(sharedTarget);
        secondSharedOwner._componentsToTurnOff.Add(sharedTarget);
        using (var sharedVisibility = new EditorTriggerVisibility(_ => false))
        {
            sharedVisibility.Observe(sharedOwner);
            sharedVisibility.Observe(secondSharedOwner);
            sharedVisibility.Observe(sharedOwner);
            sharedVisibility.Advance(4, double.PositiveInfinity);
            check(
                !sharedVisibility.Pending && sharedVisibility.ComponentCount == 1 && sharedVisibility.RevealedCount == 1,
                "Shared targets and duplicate owner observations are processed once"
            );
        }
    }
}
