using UnityEngine;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorHudChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Exercise the production HUD implementation with managed component doubles;
        // no Unity process, game scene or server is started.
        var root = new GameObject();
        var child = new GameObject();
        root.Children.Add(child);
        var native = child.AddComponent<CanvasGroup>();
        native.alpha = .35f;
        native.interactable = false;
        native.blocksRaycasts = true;
        native.ignoreParentGroups = true;
        using var hud = new EditorHud();
        hud.Suppress(root.Transform);
        check(
            child.AddAttempts == 1 && root.AddAttempts == 1,
            "Existing animated HUD groups are reused without duplicate AddComponent calls"
        );
        check(
            native.alpha == 0 && !native.blocksRaycasts && !native.ignoreParentGroups,
            "Independent HUD panels inherit editor suppression"
        );
        native.alpha = 1; // Native animation between rendering passes.
        native.ignoreParentGroups = true;
        hud.Suppress(root.Transform);
        check(
            native.alpha == 0 && !native.ignoreParentGroups && child.AddAttempts == 1,
            "Native animation cannot reveal HUD on the next render pass"
        );
        var owned = root.GetComponent<CanvasGroup>();
        hud.Dispose();
        check(
            native && native.alpha == .35f && !native.interactable && native.blocksRaycasts && native.ignoreParentGroups,
            "Closing restores the exact original native HUD state and retains its component"
        );
        check(
            owned && owned.alpha == 1 && owned.interactable && owned.blocksRaycasts,
            "Closing retains editor-created groups for native fade lifetimes"
        );
        hud.Dispose();
        check(native && native.alpha == .35f, "Repeated cleanup does not change restored native state");
        owned.alpha = .6f; // A native fade resumes after editor suppression ends.
        hud.Suppress(root.Transform);
        check(root.AddAttempts == 1 && owned.alpha == 0, "Reopening reuses the retained group without accumulating components");
        hud.Dispose();
        check(owned && owned.alpha == .6f, "A second close restores the resumed native fade state");

        var rejected = new GameObject { RejectAdd = true };
        hud.Suppress(rejected.Transform);
        check(rejected.AddAttempts == 1, "A rejected CanvasGroup addition returns safely instead of throwing each frame");
        rejected.RejectAdd = false;
        hud.Suppress(rejected.Transform);
        check(rejected.GetComponent<CanvasGroup>().alpha == 0, "A later valid HUD target recovers after an unavailable component");
        UnityEngine.Object.Destroy(rejected.GetComponent<CanvasGroup>());
        hud.Suppress(rejected.Transform);
        check(
            rejected.GetComponent<CanvasGroup>().alpha == 0,
            "A recreated native HUD component is tracked instead of dereferencing a destroyed one"
        );
        UnityEngine.Object.Destroy(rejected);
        hud.Suppress(rejected.Transform);
        hud.Dispose();
        check(true, "Destroyed HUD targets and map teardown clean up without Unity null errors");
    }
}
