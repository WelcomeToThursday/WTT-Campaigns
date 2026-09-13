using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.DI.Annotations;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Web.Authoring;

public sealed class StandaloneAssortDraft
{
    public long Revision { get; set; }
    public SeasonDefinition Definition { get; set; } =
        new()
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            FormatVersion = 3,
            Name = "Standalone trader assortments",
        };
}

[Injectable(InjectionType.Singleton)]
public sealed class StandaloneAssortRepository(SeasonRepository repository, TraderOfferCatalogue catalogue)
{
    private readonly object _gate = new();
    private readonly string _file = Path.Combine(repository.ModDirectory, "creator", "assorts", "draft.json");

    public StandaloneAssortDraft Open()
    {
        lock (_gate)
            return File.Exists(_file)
                ? JsonConvert.DeserializeObject<StandaloneAssortDraft>(File.ReadAllText(_file))
                    ?? throw new InvalidDataException("The standalone assortment draft is unreadable.")
                : new();
    }

    public StandaloneAssortDraft Save(StandaloneAssortDraft draft)
    {
        lock (_gate)
        {
            if (Open().Revision != draft.Revision)
                throw new InvalidOperationException(
                    "This assortment draft changed in another editor. Reload the saved draft before saving again."
                );
            var copy = SeasonCompiler.Copy(draft);
            copy.Revision++;
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(copy, Formatting.Indented));
            if (bytes.Length > 32 * 1024 * 1024)
                throw new InvalidOperationException("The standalone workspace exceeds 32 MiB. Export and clear completed trader drafts.");
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllBytes(_file + ".tmp", bytes);
            if (File.Exists(_file))
                File.Copy(_file, _file + ".bak", true);
            File.Move(_file + ".tmp", _file, true);
            return copy;
        }
    }

    public byte[] Export(string trader, long revision)
    {
        lock (_gate)
        {
            var draft = Open();
            if (draft.Revision != revision)
                throw new InvalidOperationException("The saved draft changed. Reload it before exporting.");
            if (!catalogue.HasTrader(trader) || !draft.Definition.TraderAssorts.Any(a => a.TraderId == trader && a.ReplaceExisting))
                throw new InvalidOperationException("Open this trader in edit mode and save its standalone assortment before exporting.");
            var offers = draft.Definition.TraderOffers.Where(o => o.TraderId == trader).ToList();
            var validation = new SeasonValidationResult();
            var definition = new SeasonDefinition { FormatVersion = 3, TraderOffers = offers };
            TraderOfferRules.Validate(definition, validation);
            foreach (var offer in offers)
                catalogue.Validate(offer, validation);
            if (!validation.CanPublish || validation.Issues.Any(i => i.Severity == "dependency"))
                throw new InvalidOperationException(
                    "Fix the assortment before exporting: " + string.Join("; ", validation.Issues.Select(i => i.Message).Take(5))
                );
            return ExportOffers(offers);
        }
    }

    internal static byte[] ExportOffers(IEnumerable<CampaignTraderOffer> offers)
    {
        var items = new List<NativeItem>();
        var barter = new Dictionary<string, List<List<NativeBarter>>>();
        var loyalty = new Dictionary<string, int>();
        foreach (var offer in offers)
        {
            var tree = SeasonCompiler.Copy(offer.Items);
            var root = tree.Single(i => i.Id == offer.Id);
            root.ParentId = root.SlotId = "hideout";
            root.Location = null;
            root.Upd ??= new();
            root.Upd.StackObjectsCount = offer.Stock;
            root.Upd.UnlimitedCount = offer.UnlimitedStock;
            root.Upd.BuyRestrictionMax = offer.PurchaseLimit > 0 ? offer.PurchaseLimit : null;
            root.Upd.BuyRestrictionCurrent = 0;
            items.AddRange(tree);
            barter[offer.Id] = SeasonCompiler.Copy(offer.Barter);
            loyalty[offer.Id] = offer.Loyalty;
        }
        return Encoding.UTF8.GetBytes(
            JsonConvert.SerializeObject(
                new
                {
                    nextResupply = 0,
                    items,
                    barter_scheme = barter,
                    loyal_level_items = loyalty,
                },
                Formatting.Indented
            )
        );
    }
}
