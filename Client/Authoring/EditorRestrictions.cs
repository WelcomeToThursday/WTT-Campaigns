using System.Reflection;
using EFT;
using EFT.HealthSystem;
using EFT.InputSystem;
using EFT.UI.BattleTimer;
using HarmonyLib;

namespace WTT.Campaigns.Client.Authoring;

internal static class EditorRestrictions
{
    private static readonly HashSet<string> MovementCommands = new(StringComparer.Ordinal)
    {
        "None",
        "ToggleSpeed",
        "DecreaseWalkSpeed",
        "ToggleDuck",
        "ToggleSprinting",
        "EndSprinting",
        "ToggleProne",
        "ResetLookDirection",
        "NextWalkPose",
        "PreviousWalkPose",
        "Jump",
        "ToggleLeanRight",
        "ToggleLeanLeft",
        "ToggleWalk",
        "EndWalk",
        "EndLeanLeft",
        "EndLeanRight",
        "RestorePose",
        "MakeScreenshot",
        "Vaulting",
        "Climb",
    };

    internal static void Filter(List<ECommand> commands) => commands.RemoveAll(c => !MovementCommands.Contains(c.ToString()));

    internal static void Enable()
    {
        var harmony = new Harmony("com.wtt.campaigns.editor.restrictions");
        void Patch(Type type, string name, string prefix)
        {
            var targets = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var found = false;
            foreach (var target in targets)
            {
                if (target.Name != name)
                    continue;
                found = true;
                var handler = prefix == nameof(NoAction) && target.ReturnType == typeof(Task) ? nameof(NoTask) : prefix;
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(EditorRestrictions), handler));
            }
            if (!found)
                throw new MissingMethodException(type.FullName, name);
        }
        Patch(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage), nameof(NoDamage));
        Patch(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeEnergy), nameof(NoDrain));
        Patch(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHealth), nameof(NoDrain));
        Patch(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill), nameof(NoAction));
        Patch(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHydration), nameof(NoDrain));
        Patch(typeof(Stamina), nameof(Stamina.Consume), nameof(NoConsumption));
        Patch(typeof(LocalGame), nameof(LocalGame.Stop), nameof(Stop));
        Patch(typeof(TarkovApplication), nameof(TarkovApplication.ShowSessionResult), nameof(ReturnHome));
        Patch(typeof(TimerPanel), nameof(TimerPanel.UpdateTimer), nameof(NoAction));
        Patch(typeof(TimerPanel), nameof(TimerPanel.SetTimerText), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.ActivateBotsByWave), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.TryToSpawnInZoneAndDelay), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.SpawnBotsInZoneOnPositions), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.TrySpawnFreeAndDelay), nameof(NoAction));
    }

    private static bool NoDamage(ref float __result)
    {
        if (!EditorMode.Active)
            return true;
        __result = 0;
        return false;
    }

    private static bool NoDrain(float value) => !EditorMode.Active || value >= 0;

    private static bool NoConsumption(ref float __result)
    {
        if (!EditorMode.Active)
            return true;
        __result = 0;
        return false;
    }

    private static bool NoTask(ref Task __result)
    {
        if (!EditorMode.Active)
            return true;
        __result = Task.CompletedTask;
        return false;
    }

    private static bool NoAction() => !EditorMode.Active;

    private static bool ReturnHome(TarkovApplication __instance, ref Task __result)
    {
        if (!EditorMode.Active)
            return true;
        __result = EditorMode.Instance.CompleteMapExit(__instance);
        return false;
    }

    private static bool Stop() => !EditorMode.Active || EditorMode.Unloading;
}
