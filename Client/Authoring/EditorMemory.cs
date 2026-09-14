using HarmonyLib;
using UnityEngine.Scripting;

namespace WTT.Campaigns.Client.Authoring;

internal static class EditorMemory
{
    private static readonly EditorCollectionPolicy Policy = new();
    private static bool _applying;

    internal static void Enable()
    {
        var harmony = new Harmony("com.wtt.campaigns.editor.memory");
        harmony.Patch(AccessTools.PropertySetter(typeof(GarbageCollector), nameof(GarbageCollector.GCMode)),
            prefix: new HarmonyMethod(typeof(EditorMemory), nameof(BeforeMode)));
        // The native wrapper skips its setter when GC is already enabled. Record
        // that request too, so returning to the menu cannot restore a stale Disabled mode.
        harmony.Patch(AccessTools.PropertySetter(typeof(InGameMemoryManagement), nameof(InGameMemoryManagement.GCEnabled)),
            prefix: new HarmonyMethod(typeof(EditorMemory), nameof(BeforeNativeMode)));
    }

    private static void BeforeNativeMode(bool value)
    {
        if (!_applying) Policy.Filter(value ? GarbageCollector.Mode.Enabled : GarbageCollector.Mode.Disabled);
    }

    private static void BeforeMode(ref GarbageCollector.Mode value)
    {
        if (!_applying) value = Policy.Filter(value);
    }

    internal static void Tick(bool active)
    {
        var mode = Policy.Transition(active, GarbageCollector.GCMode);
        if (mode == null) return;
        _applying = true;
        try
        {
            GarbageCollector.GCMode = mode.Value;
            if (EditorDiagnostics.Enabled)
                Plugin.LogInfo(active ? "Editor memory: automatic garbage collection enabled for this editor map."
                    : "Editor memory: restored requested garbage collection mode " + mode.Value + ".");
        }
        finally { _applying = false; }
    }
}
