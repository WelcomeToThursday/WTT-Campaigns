using SPTarkov.Server.Core.Models.Common;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Seasons;

public sealed partial class SeasonContentService
{
    private readonly IsolatedLocaleLayers _isolatedLocales = new();

    public void RequireTestContentReady(SeasonDefinition definition)
    {
        foreach (var part in new[] { definition }.Concat(definition.MissionLinks.Select(l => l.Package)))
        foreach (var item in part.ImportedItems.Values)
            if (item.Properties?.Prefab?.Path is { } path && _itemBundles.ChangedSinceStartup(path))
                throw new InvalidOperationException(
                    "An external item bundle changed after startup. Restart SPT and the game manually before applying this draft."
                );
        var validation = Validate(definition);
        if (!validation.CanActivate)
            throw new InvalidOperationException(
                "Fix the draft or install its dependencies before testing: "
                    + string.Join("; ", validation.Issues.Where(i => i.Severity != "warning").Take(8).Select(i => i.Message))
            );
    }

    /// <summary>Registers the copied campaign's native content for one disposable backend.</summary>
    public IDisposable RegisterIsolated(SeasonDefinition definition)
    {
        if (!repository.IsIsolatedSnapshot(definition.Id))
            throw new InvalidOperationException("Register a disposable campaign snapshot before its native content.");
        var validation = Validate(definition);
        if (!validation.CanActivate)
            throw new InvalidOperationException(
                "The campaign test has unavailable content: "
                    + string.Join("; ", validation.Issues.Where(i => i.Severity != "warning").Select(i => i.Message).Take(8))
            );

        var definitions = new[] { definition }.Concat(definition.MissionLinks.Select(l => l.Package)).ToArray();
        var ids = definitions
            .SelectMany(d => d.Items)
            .Select(i => new MongoId(i.Id))
            .Concat(definitions.SelectMany(d => d.ImportedItems.Keys).Select(id => new MongoId(id)))
            .ToHashSet();
        var beforeItems = ids.ToDictionary(id => id, id => templates.Items.GetValueOrDefault(id));
        var beforeOwners = ids.ToDictionary(id => id.ToString(), id => _owners.GetValueOrDefault(id.ToString()));
        var beforeHandbook = templates.Handbook.Items.Where(i => ids.Contains(i.Id)).ToArray();
        var crateIds = definitions.SelectMany(d => d.Crates).Select(c => new MongoId(c.ItemId)).ToHashSet();
        var beforeCrates = crateIds.ToDictionary(id => id, id => inventory.RandomLootContainers.GetValueOrDefault(id));
        var localeRegistrations = new List<IDisposable>();
        var craftIds = definitions.SelectMany(d => d.Crafts).Select(c => c.Id).ToHashSet();
        var beforeCrafts = hideout.Production.Recipes.Where(r => craftIds.Contains(r.Id.ToString())).ToArray();
        var beforeCraftOwners = craftIds.ToDictionary(id => id, id => _craftOwners.GetValueOrDefault(id));

        void Restore()
        {
            hideout.Production.Recipes.RemoveAll(r => craftIds.Contains(r.Id.ToString()));
            hideout.Production.Recipes.AddRange(beforeCrafts);
            foreach (var id in craftIds)
                if (beforeCraftOwners[id] is { } owner)
                    _craftOwners[id] = owner;
                else
                    _craftOwners.Remove(id);
            foreach (var id in ids)
            {
                if (beforeItems[id] is { } item)
                    templates.Items[id] = item;
                else
                    templates.Items.Remove(id);
                if (beforeOwners[id.ToString()] is { } owner)
                    _owners[id.ToString()] = owner;
                else
                    _owners.Remove(id.ToString());
            }
            templates.Handbook.Items.RemoveAll(item => ids.Contains(item.Id));
            templates.Handbook.Items.AddRange(beforeHandbook);
            foreach (var id in crateIds)
            {
                if (beforeCrates[id] is { } crate)
                    inventory.RandomLootContainers[id] = crate;
                else
                    inventory.RandomLootContainers.Remove(id);
            }
            foreach (var registration in localeRegistrations)
                registration.Dispose();
        }
        try
        {
            foreach (var part in definitions)
            foreach (var language in localeTable.Global.Keys)
                localeRegistrations.Add(
                    _isolatedLocales.Install(language, locales.GetLocaleDb(language), SeasonCompiler.Texts(part, language))
                );
            Register(definition, true);
            if (ids.Any(id => !templates.Items.ContainsKey(id)))
                throw new InvalidOperationException("Some campaign test item templates could not be registered.");
            return new IsolatedContentRegistration(Restore);
        }
        catch
        {
            Restore();
            throw;
        }
    }

    private sealed class IsolatedContentRegistration(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
