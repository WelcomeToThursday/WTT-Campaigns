using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.UI;
using SeasonalPerks.Client.Patches.Health;
using SeasonalPerks.Client.Patches.Hideout;
using SeasonalPerks.Client.Patches.Items;
using SeasonalPerks.Client.Patches.Movement;
using SeasonalPerks.Client.Patches.Session;
using SeasonalPerks.Client.Patches.Skills;
using SeasonalPerks.Client.Patches.UI;

namespace SeasonalPerks.Client.Patches;

internal static class PatchRegistration
{
    internal static void EnableAll()
    {
        EnableSession();
        EnableHealth();
        EnableSkills();
        EnableMovement();
        EnableItems();
        EnableHideout();
        EnableUi();
    }

    private static void EnableSession()
    {
        new BackendIdentity().Enable();
        new SptRequestIdentity().Enable();
    }

    private static void EnableHealth()
    {
        new EnergyDrainPatch().Enable();
        new HydrationDrainPatch().Enable();
        new DamageContextPatch().Enable();
        new InjuryProbabilityPatch().Enable();
        new FreshWoundPatch().Enable();
        new ConsumableRegenerationStartPatch().Enable();
        new ConsumableRegenerationTickPatch().Enable();
    }

    private static void EnableSkills()
    {
        new SkillProgressPatch().Enable();
        new ProfileExperiencePatch().Enable();
        new TreatmentExperiencePatch().Enable();
        new RaidExperiencePatch().Enable();
    }

    private static void EnableMovement()
    {
        new BushTriggerPatch(nameof(TreeInteractive.OnTriggerEnter)).Enable();
        new BushTriggerPatch(nameof(TreeInteractive.OnTriggerExit)).Enable();
        new BushMovementPatch().Enable();
        new BushCleanupPatch().Enable();
        new BushSoundPatch(nameof(TreeInteractive.OnTriggerEnter)).Enable();
        new BushSoundPatch("IPhysicsTriggerWithStay.OnTriggerStay").Enable();
        new BushSoundPatch(nameof(TreeInteractive.PlaySoundBank)).Enable();
        new SprintSpeedPatch().Enable();
        new StaminaConsumptionPatch(nameof(Stamina.Consume)).Enable();
        new StaminaConsumptionPatch(nameof(Stamina.Process)).Enable();
        new StaminaCapacityPatch(nameof(Physical.GetStaminaCapacityFunc)).Enable();
        new StaminaCapacityPatch(nameof(Physical.GetHandsCapacityFunc)).Enable();
        new StaminaRestorationPatch(nameof(Physical.GetStaminaRestorationFunc)).Enable();
        new StaminaRestorationPatch(nameof(Physical.GetHandsRestorationFunc)).Enable();
    }

    private static void EnableItems()
    {
        new HubDocumentPickupPatch().Enable();
        new HubDocumentMergePatch().Enable();
        new HubDocumentTransferPatch().Enable();
        new HubDocumentSplitPatch().Enable();
        new HubDocumentRaidPatch(nameof(EftClientBackendSession.LocalRaidStarted)).Enable();
        new HubDocumentRaidPatch(nameof(EftClientBackendSession.LocalRaidEnded)).Enable();
        new ConsumableUsePatch().Enable();
        new MedicineCompletionPatch().Enable();
        new SecureGridPatch().Enable();
        new SecureMovePatch().Enable();
        new SecureAddPatch().Enable();
        new SecureTransferPatch().Enable();
        new ItemResourcePatch(typeof(ActiveHealthController.MedEffect), nameof(ActiveHealthController.MedEffect.RegularUpdate)).Enable();
        new ItemResourcePatch(typeof(ActiveHealthController.MedEffect), nameof(ActiveHealthController.MedEffect.Residue)).Enable();
        new ItemResourcePatch(typeof(OfflineHealthController.MedEffect), nameof(OfflineHealthController.MedEffect.Started)).Enable();
        new KeyUsagePatch(typeof(WorldInteractiveObject)).Enable();
        new KeyUsagePatch(typeof(KeycardDoor)).Enable();
    }

    private static void EnableHideout()
    {
        new FoundInRaidPatch().Enable();
    }

    private static void EnableUi()
    {
        new MenuEntry(typeof(MenuScreen)).Enable();
        new SkillsTabPatch().Enable();
        new SeasonalUiInputPatch().Enable();
    }
}
