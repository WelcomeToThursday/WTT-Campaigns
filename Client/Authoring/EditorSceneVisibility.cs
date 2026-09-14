using HarmonyLib;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

// Free flight can leave the baked visibility cells entirely. Keep native desired
// visibility and distance/LOD processing, but bypass the baked occlusion decision.
internal sealed class EditorSceneVisibility : IDisposable
{
    private static EditorSceneVisibility? _active;
    private static volatile bool _editing; // TestAABB is also called by native worker jobs.
    private static bool _patched;
    private readonly Dictionary<PerfectCullingCrossSceneGroup, PerfectCullingBakeGroup[]> _owners = new();
    private readonly HashSet<PerfectCullingBakeGroup> _groups = new();
    private readonly EditorTriggerVisibility _triggers;

    internal EditorSceneVisibility(Func<GameObject, bool> hidden)
    {
        Enable();
        CompleteGridJobs();
        _triggers = new EditorTriggerVisibility(hidden);
        _active = this;
        _editing = true;
    }

    internal void Discover()
    {
        foreach (var owner in Resources.FindObjectsOfTypeAll<DisablerCullingObject>())
            if (owner && owner.gameObject.scene.IsValid() && owner.gameObject.scene.isLoaded)
                _triggers.Observe(owner);
        foreach (var owner in PerfectCullingCrossSceneGroup.AllCrossGroups)
            Observe(owner);
        RefreshGrid();
        Plugin.LogInfo($"Editor visibility: {_owners.Count} baked owners, {_groups.Count} baked groups; "
            + $"{_triggers.SwitchCount} trigger switches, {_triggers.ComponentCount} components, {_triggers.ObjectCount} objects; "
            + $"{_triggers.RevealedCount} disabled trigger targets revealed.");
    }

    private static bool BeforeTrigger(DisablerCullingObject __instance)
    {
        if (_active == null) return true;
        _active._triggers.Observe(__instance);
        return false;
    }

    private void Observe(PerfectCullingCrossSceneGroup owner, bool initialized = false)
    {
        if (!owner || owner.bakeGroups == null) return;
        if (_owners.TryGetValue(owner, out var previous))
        {
            if (!initialized && previous == owner.bakeGroups) return;
            foreach (var group in previous) _groups.Remove(group);
        }
        _owners[owner] = owner.bakeGroups;
        foreach (var group in owner.bakeGroups)
            if (group?.RuntimeGroupData != null && _groups.Add(group)) group.Toggle(true);
    }

    private static void BeforeToggle(PerfectCullingBakeGroup __instance, ref bool rendererEnabled)
    {
        // IsEnabled retains the native requested state for restoration. Never
        // activate GameObjects here: authored removals must remain hidden.
        if (_active?._groups.Contains(__instance) == true) rendererEnabled = true;
    }

    private static bool BeforeTest(ref bool __result)
    {
        if (!_editing) return true;
        __result = true;
        return false;
    }

    private static void AfterInitialize(PerfectCullingCrossSceneGroup __instance) => _active?.Observe(__instance, true);

    private static void CompleteGridJobs()
    {
        var grid = CullingGridContentSwitcher.Instance;
        if (grid == null) return;
        grid._dynamicVisibilityUpdateJobHandle.Complete();
        grid._staticVisibilityJobUpdateHandle.Complete();
    }

    private static void RefreshGrid()
    {
        var grid = CullingGridContentSwitcher.Instance;
        if (grid == null) return;
        CompleteGridJobs();
        // Refresh even if the camera has not crossed into a new baked cell.
        CullingGridContentSwitcher._lastCullingCellId = int.MinValue;
        grid.Update();
        grid.UpdateStaticObjects();
    }

    internal static void Enable()
    {
        if (_patched) return;
        var harmony = new Harmony("com.wtt.campaigns.editor.scene-visibility");
        harmony.Patch(AccessTools.Method(typeof(DisablerCullingObject), nameof(DisablerCullingObject.SetComponentsEnabled)),
            prefix: new HarmonyMethod(typeof(EditorSceneVisibility), nameof(BeforeTrigger)));
        harmony.Patch(AccessTools.Method(typeof(PerfectCullingBakeGroup), nameof(PerfectCullingBakeGroup.Toggle)),
            prefix: new HarmonyMethod(typeof(EditorSceneVisibility), nameof(BeforeToggle)));
        harmony.Patch(AccessTools.Method(typeof(CullingGridVisibilityQueryResult), nameof(CullingGridVisibilityQueryResult.TestAABB)),
            prefix: new HarmonyMethod(typeof(EditorSceneVisibility), nameof(BeforeTest)));
        harmony.Patch(AccessTools.Method(typeof(PerfectCullingCrossSceneGroup), nameof(PerfectCullingCrossSceneGroup.InitializeRuntime)),
            postfix: new HarmonyMethod(typeof(EditorSceneVisibility), nameof(AfterInitialize)));
        _patched = true;
    }

    public void Dispose()
    {
        if (_active != this) return;
        try { CompleteGridJobs(); }
        finally { _active = null; _editing = false; }
        _triggers.Dispose();
        foreach (var entry in _owners)
        {
            if (!entry.Key || entry.Key.bakeGroups != entry.Value) continue;
            foreach (var group in entry.Value)
                if (group != null && _groups.Contains(group))
                    try { group.Toggle(group.IsEnabled); }
                    catch (Exception error) { Plugin.Error(error); }
        }
        _owners.Clear();
        _groups.Clear();
        RefreshGrid();
    }
}
