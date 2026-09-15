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

    internal static void Filter(List<ECommand> commands)
    {
        // The editor handles raw Escape after native input has run. Never let the
        // same press open EFT's menu underneath a returning combat preview.
        commands.Remove(ECommand.Escape);
        if (!RaidEditor.AiPlaytestActive)
            commands.RemoveAll(c => !MovementCommands.Contains(c.ToString()));
        // Playtests use temporary equipment and must retain native inventory,
        // looting, weapon, medical and interaction commands.
    }

    internal static void Enable()
    {
        Encounters.EncounterNative.Install();
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
        foreach (
            var method in typeof(ActiveHealthController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        )
            if (method.Name == nameof(ActiveHealthController.ChangeHealth))
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(EditorRestrictions), nameof(PreviewHealthChanged)));
        Patch(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill), nameof(PreviewKill));
        Patch(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHydration), nameof(NoDrain));
        Patch(typeof(Stamina), nameof(Stamina.Consume), nameof(NoConsumption));
        Patch(typeof(LocalGame), nameof(LocalGame.Stop), nameof(Stop));
        Patch(typeof(EFT.UI.InventoryScreen), nameof(EFT.UI.InventoryScreen.Show), nameof(InventoryAllowed));
        Patch(typeof(EftGamePlayerOwner), nameof(EftGamePlayerOwner.ShowInventoryScreenLoot), nameof(InventoryAllowed));
        Patch(typeof(Player), nameof(Player.SetInventoryOpened), nameof(InventoryOpened));
        Patch(typeof(TarkovApplication), nameof(TarkovApplication.ShowSessionResult), nameof(ReturnHome));
        Patch(typeof(TimerPanel), nameof(TimerPanel.UpdateTimer), nameof(NoAction));
        Patch(typeof(TimerPanel), nameof(TimerPanel.SetTimerText), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.ActivateBotsByWave), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.TryToSpawnInZoneAndDelay), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.SpawnBotsInZoneOnPositions), nameof(NoAction));
        Patch(typeof(BotSpawner), nameof(BotSpawner.TrySpawnFreeAndDelay), nameof(NoAction));
    }

    private static bool DamageAllowed(ActiveHealthController health) =>
        !EditorMode.Active
        || RaidEditor.AiPreviewActive && health.Player != Plugin.Player
        || RaidEditor.AiPlaytestActive && health.Player == Plugin.Player;

    private static bool NoDamage(ActiveHealthController __instance, ref float __result)
    {
        if (DamageAllowed(__instance))
            return true;
        __result = 0;
        return false;
    }

    private static bool NoDrain(ActiveHealthController __instance, float value) => DamageAllowed(__instance) || value >= 0;

    private static bool PreviewKill(ActiveHealthController __instance)
    {
        if (!EditorMode.Active)
            return true;
        if (__instance.Player != Plugin.Player)
            return RaidEditor.AiPreviewActive;
        if (RaidEditor.AiPlaytestActive)
            RaidEditor.Instance?.AiDefeated();
        // Kill is the native terminal transition for direct kills and damage effects.
        // Reset on the next editor update, outside this health-controller call stack.
        return false;
    }

    private static void PreviewHealthChanged(ActiveHealthController __instance)
    {
        if (!RaidEditor.AiPlaytestActive || __instance.Player != Plugin.Player)
            return;
        // Health-rate effects may change a vital part without invoking native Kill.
        if (__instance.GetBodyPartHealth(EBodyPart.Head).AtMinimum || __instance.GetBodyPartHealth(EBodyPart.Chest).AtMinimum)
            RaidEditor.Instance?.AiDefeated();
    }

    private static bool NoConsumption(ref float __result)
    {
        if (!EditorMode.Active || RaidEditor.AiPreviewActive)
            return true;
        __result = 0;
        return false;
    }

    private static bool NoTask(ref Task __result)
    {
        if (!EditorMode.Active || MissionGameplayActive)
            return true;
        __result = Task.CompletedTask;
        return false;
    }

    private static bool NoAction() => !EditorMode.Active || MissionGameplayActive;

    private static bool InventoryAllowed() => !EditorMode.Active || RaidEditor.AiPlaytestActive;

    private static bool InventoryOpened(bool opened) => InventoryAllowed() || !opened;

    private static bool MissionGameplayActive => RaidEditor.MissionTestActive && RaidEditor.AiPlaytestActive;

    private static bool ReturnHome(TarkovApplication __instance, ref Task __result)
    {
        if (!EditorMode.Active)
            return true;
        __result = EditorMode.Instance.CompleteMapExit(__instance);
        return false;
    }

    private static bool Stop() => !EditorMode.Active || EditorMode.Unloading;
}
