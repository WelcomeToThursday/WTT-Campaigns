using System.Runtime.CompilerServices;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Seasons;

[Injectable(InjectionType.Singleton)]
public sealed class CampaignQuestLootService(
    SeasonService seasons,
    SeasonRepository repository,
    SaveServer saves,
    BotGeneratorHelper inventory
)
{
    private readonly ConditionalWeakTable<SeasonRuntimeSnapshot, List<CampaignQuestLoot>> _rules = new();

    public void Add(MongoId sessionId, string role, BotBase bot)
    {
        if (!seasons.IsSeasonal(sessionId.ToString()) || bot.Inventory == null || !bot.Id.HasValue)
            return;
        var runtime = repository.Runtime(seasons.CharacterSeasonId(sessionId.ToString()));
        var rules = _rules.GetValue(runtime, r => r.Definition.QuestLoot);
        if (rules.Count == 0)
            return;
        var pmc = saves.GetProfile(sessionId).CharacterData!.PmcData!;
        foreach (var rule in rules)
        {
            if (
                !rule.BotRoles.Contains(role, StringComparer.OrdinalIgnoreCase)
                || pmc.Quests?.Any(q =>
                    q.QId.ToString() == rule.QuestId && q.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish
                ) != true
                || bot.Inventory.Items.Any(i => i.Template.ToString() == rule.ItemTemplate)
            )
                continue;
            var id = new MongoId();
            var template = new MongoId(rule.ItemTemplate);
            inventory.AddItemWithChildrenToEquipmentSlot(
                bot.Id.Value,
                [EquipmentSlots.Backpack, EquipmentSlots.TacticalVest, EquipmentSlots.Pockets],
                id,
                template,
                [
                    new Item
                    {
                        Id = id,
                        Template = template,
                        Upd = new Upd { StackObjectsCount = 1, SpawnedInSession = true },
                    },
                ],
                bot.Inventory
            );
        }
    }
}
