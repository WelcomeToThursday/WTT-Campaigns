using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.UI;
using WTT.Campaigns.Client.Patches.Health;
using WTT.Campaigns.Client.Patches.Hideout;
using WTT.Campaigns.Client.Patches.Items;
using WTT.Campaigns.Client.Patches.Movement;
using WTT.Campaigns.Client.Patches.Session;
using WTT.Campaigns.Client.Patches.Skills;
using WTT.Campaigns.Client.Patches.UI;

namespace WTT.Campaigns.Client.Patches;

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
        new Story.StoryTraderPatch().Enable();
        new Story.StoryCollectiblePatch().Enable();
    }

    private static void EnableSession()
    {
        new BackendIdentity().Enable();
        new SptRequestIdentity().Enable();
        new AuthoringNotificationSocket().Enable();
        new AuthoringNotificationReply().Enable();
        new BotDifficultyFallbackPatch().Enable();
        new RaidLoadRecoveryPatch().Enable();
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
        new WTT.Campaigns.Client.Story.StoryTasksPatch().Enable();
        new TaskGroupingShowPatch().Enable();
        new TaskGroupingRefreshPatch(nameof(QuestsListView.UpdateVisibility)).Enable();
        new TaskGroupingRefreshPatch(nameof(QuestsListView.QuestAddedHandler)).Enable();
        new TaskGroupingSelectPatch().Enable();
        new TraderSpendingTooltipPatch().Enable();
        new TaskTierBadgePatch().Enable();
        new MenuEntry(typeof(MenuScreen)).Enable();
        new SkillsTabPatch().Enable();
        new CampaignUiInputPatch().Enable();
    }
}
