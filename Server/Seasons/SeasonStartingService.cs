using SeasonalPerks.Server.Hub;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace SeasonalPerks.Server.Seasons;

[Injectable(InjectionType.Singleton)]
public sealed class SeasonStartingService(
    SeasonRepository repository,
    SaveServer saves,
    ICloner cloner,
    InventoryHelper inventory,
    TemplateTable templates
)
{
    public async Task Apply(MongoId id, string side, string seasonId = "")
    {
        var original = saves.GetProfile(id);
        var season = repository.Runtime(seasonId).Definition;
        var key = "wttSeasonalStarting:" + season.Id;
        if (original.CharacterData!.PmcData!.ExtensionData.ContainsKey(key))
        {
            return;
        }

        var staged = cloner.Clone(original)!;
        var pmc = staged.CharacterData!.PmcData!;
        var setup = side == "Bear" ? season.Starting.Bear : season.Starting.Usec;
        foreach (var entry in setup.Items)
        {
            var item = new Item
            {
                Id = new MongoId(),
                Template = new MongoId(entry.Template),
                Upd = new Upd { StackObjectsCount = entry.Count },
            };
            if (entry.Slot.Length == 0)
            {
                var output = new ItemEventRouterResponse
                {
                    ProfileChanges = new()
                    {
                        [id] = new ProfileChange
                        {
                            Items = new ItemChanges
                            {
                                NewItems = [],
                                ChangedItems = [],
                                DeletedItems = [],
                            },
                        },
                    },
                };
                inventory.AddItemToStash(
                    id,
                    new AddItemDirectRequest
                    {
                        ItemWithModsToAdd = [item],
                        FoundInRaid = false,
                        UseSortingTable = false,
                    },
                    pmc,
                    output
                );
                if (output.Warnings?.Count > 0)
                {
                    throw new InvalidOperationException("Starting items do not fit the selected starter stash.");
                }
            }
            else
            {
                var root = pmc.Inventory!.Items!.Single(i => i.Id == pmc.Inventory.Equipment);
                var equipment = templates.Items[root.Template];
                var slot = equipment.Properties?.Slots?.FirstOrDefault(s => s.Name == entry.Slot);
                var allowed = slot?.Properties?.Filters?.SelectMany(f => f.Filter ?? []).Select(id => id.ToString()).ToHashSet() ?? [];
                var ancestors = new HashSet<string>();
                var current = item.Template;
                while (ancestors.Add(current.ToString()) && templates.Items.TryGetValue(current, out var template))
                {
                    current = template.Parent;
                }

                if (slot == null || (allowed.Count > 0 && !allowed.Overlaps(ancestors)))
                {
                    throw new InvalidOperationException("Starting equipment is incompatible with slot " + entry.Slot + ".");
                }

                var remove = pmc
                    .Inventory.Items!.Where(i => i.ParentId == root.Id.ToString() && i.SlotId == entry.Slot)
                    .Select(i => i.Id.ToString())
                    .ToHashSet();
                bool changed;
                do
                {
                    changed = false;
                    foreach (var old in pmc.Inventory.Items!)
                    {
                        if (old.ParentId != null && remove.Contains(old.ParentId))
                        {
                            changed |= remove.Add(old.Id.ToString());
                        }
                    }
                } while (changed);
                pmc.Inventory.Items!.RemoveAll(i => remove.Contains(i.Id.ToString()));
                item.ParentId = root.Id.ToString();
                item.SlotId = entry.Slot;
                pmc.Inventory.Items.Add(item);
            }
        }
        foreach (var skill in setup.Skills)
        {
            var target = pmc.Skills!.Common!.Single(s => s.Id == Enum.Parse<SkillTypes>(skill.Key));
            target.Progress = skill.Value * 100d;
        }
        pmc.ExtensionData[key] = SeasonRepository.GameplayHash(season);
        HubProfileStore.Replace(saves, id, original, staged);
        try
        {
            await saves.SaveProfileAsync(id);
        }
        catch
        {
            HubProfileStore.Replace(saves, id, staged, original);
            try
            {
                await saves.SaveProfileAsync(id);
            }
            catch { }
            throw;
        }
    }
}
