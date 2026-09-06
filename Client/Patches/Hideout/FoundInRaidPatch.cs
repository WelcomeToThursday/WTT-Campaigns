using System.Reflection;
using EFT.Hideout;
using HarmonyLib;
using SeasonalPerks.Shared;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Hideout;

internal class FoundInRaidPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.PropertyGetter(
            typeof(ItemRequirement),
            nameof(ItemRequirement.IsSpawnedInSession)
        );

    [PatchPostfix]
    private static void Postfix(ref bool __result)
    {
        // The same getter supplies inventory eligibility and the hideout requirement icon.
        // Do not mutate shared requirements or an item's actual found-in-raid status.
        if (CharacterSession.IsLoaded(Plugin.Current, "seasonal", Plugin.App?.Session?.Profile?.Id))
            __result = Plugin.Effects.HideoutRequiresFir(__result);
    }
}
