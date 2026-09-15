using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Client.Authoring.Preview;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class TraderOfferChecks
{
    private const string Tpl = "000000000000000000000011",
        ChildTpl = "000000000000000000000012",
        TraderId = "54cb50c76803fa8b248b4571";

    public static void Run(SeasonRepository repository, Action<bool, string> check)
    {
        void Reject(Action operation, string reason)
        {
            var rejected = false;
            try
            {
                operation();
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException or FormatException)
            {
                rejected = true;
            }
            check(rejected, reason);
        }
        var draft = repository.Create(false);
        var source = new List<NativeItem>
        {
            new()
            {
                Id = "000000000000000000000001",
                Template = Tpl,
                Upd = new()
                {
                    StackObjectsCount = 900,
                    UnlimitedCount = true,
                    BuyRestrictionCurrent = 8,
                    BuyRestrictionMax = 10,
                },
            },
        };
        var sourceJson = JsonConvert.SerializeObject(source);
        var offer = TraderOfferAuthoring.Create(draft.Definition, source, "Test offer", TraderId);
        check(
            sourceJson == JsonConvert.SerializeObject(source) && offer.Id != source[0].Id && draft.Definition.FormatVersion == 3,
            "Copying an offer creates an independent format-3 tree without changing native stock"
        );
        check(
            offer.Items[0].Upd!.StackObjectsCount == 1 && offer.Items[0].Upd!.BuyRestrictionCurrent == null,
            "Stock and purchase counters do not leak into preview assemblies"
        );
        var slot = new OfferContainer
        {
            Id = "mod_scope",
            Kind = "Slot",
            Allowed = [ChildTpl],
        };
        var child = TraderOfferAuthoring.Add(offer, offer.Id, slot, ChildTpl);
        Reject(() => TraderOfferAuthoring.Add(offer, offer.Id, slot, ChildTpl), "A second attachment cannot occupy a filled slot");
        var grandchild = TraderOfferAuthoring.Add(offer, child.Id, new() { Id = "nested", Kind = "Slot" }, ChildTpl);
        TraderOfferAuthoring.Remove(offer, child.Id);
        check(offer.Items.Count == 1, "Removing a parent removes all descendants");
        child = TraderOfferAuthoring.Add(offer, offer.Id, slot, ChildTpl);
        var before = TraderOfferRules.AssemblyHash(offer.Items);
        offer.Stock = 7;
        offer.PurchaseLimit = 3;
        offer.Barter[0][0].Count = 30;
        offer.UnlimitedStock = false;
        check(before == TraderOfferRules.AssemblyHash(offer.Items), "Price, stock and limits preserve assembly verification identity");
        child.Upd!.StackObjectsCount = 2;
        check(before != TraderOfferRules.AssemblyHash(offer.Items), "Assembly quantity changes invalidate verification");
        child.Upd.StackObjectsCount = 1;
        var duplicate = repository.Create(true, draft.Definition).Definition;
        check(
            duplicate.TraderOffers[0].Id != offer.Id
                && duplicate.TraderOffers[0].Items.All(i => offer.Items.All(old => old.Id != i.Id))
                && duplicate.TraderOffers[0].Items[1].ParentId == duplicate.TraderOffers[0].Id,
            "Campaign duplication remaps every owned offer identity and parent"
        );
        check(
            before == TraderOfferRules.AssemblyHash(duplicate.TraderOffers[0].Items),
            "Equivalent duplicated assemblies can reuse locally trusted verification"
        );
        var reward = new SeasonReward
        {
            Id = "000000000000000000000099",
            Enabled = false,
            Grants = [offer.Reference()],
        };
        draft.Definition.SeasonalRewards.Add(reward);
        Reject(() => TraderOfferAuthoring.Delete(draft.Definition, offer), "References in disabled rewards still protect offer deletion");
        offer.Loyalty = 3;
        TraderOfferAuthoring.SynchronizeReferences(draft.Definition);
        check(
            reward.Grants[0].Items.Count == 0 && reward.Grants[0].LoyaltyLevel == 3,
            "Rewards reference canonical offers without independent item trees"
        );
        var copied = repository.Create(true, draft.Definition).Definition;
        check(
            copied.SeasonalRewards.Last().Grants[0].Target == copied.TraderOffers[0].Id,
            "Duplicated rewards reference the duplicated offer"
        );
        check(
            !TraderOfferRules.Eligible(offer, "campaign", null, true, 4, true)
                && !TraderOfferRules.Eligible(offer, "campaign", "other", true, 4, true),
            "Ordinary and other-campaign characters cannot buy campaign offers"
        );
        check(
            !TraderOfferRules.Eligible(offer, "campaign", "campaign", false, 4, true)
                && !TraderOfferRules.Eligible(offer, "campaign", "campaign", true, 2, true),
            "Native trader access and loyalty are retained"
        );
        check(TraderOfferRules.Eligible(offer, "campaign", "campaign", true, 3, false), "Ordinary campaign stock needs no reward claim");
        offer.RequiresUnlock = true;
        check(
            !TraderOfferRules.Eligible(offer, "campaign", "campaign", true, 4, false)
                && TraderOfferRules.Eligible(offer, "campaign", "campaign", true, 4, true),
            "Reward-gated stock requires a claim"
        );
        offer.RequiresUnlock = false;
        var hash = SeasonRepository.GameplayHash(draft.Definition);
        offer.Name = "Renamed";
        check(hash == SeasonRepository.GameplayHash(draft.Definition), "Offer labels remain presentation-only");
        offer.Stock++;
        check(hash != SeasonRepository.GameplayHash(draft.Definition), "Trader stock participates in campaign gameplay identity");
        var malformed = SeasonCompiler.Copy(offer);
        malformed.Items[1].ParentId = malformed.Items[1].Id;
        var badSeason = SeasonCompiler.Copy(draft.Definition);
        badSeason.TraderOffers = [malformed];
        check(!SeasonValidator.Validate(badSeason).CanPublish, "Cyclic offer assemblies cannot publish");
        badSeason.TraderOffers[0].Items = SeasonCompiler.Copy(offer.Items);
        badSeason.TraderOffers[0].Barter[0][0].Count = double.NaN;
        check(!SeasonValidator.Validate(badSeason).CanPublish, "Non-finite barter costs cannot publish");

        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        var stock = CampaignTraderStock.Create(offer, json);
        var root = stock.Items.Single(i => i.Id.ToString() == offer.Id);
        check(
            root.ParentId == "hideout"
                && root.Upd!.StackObjectsCount == offer.Stock
                && root.Upd.BuyRestrictionMax == 3
                && root.Upd.UnlimitedCount == false,
            "Native stock is built with configured count, limit and parent markers"
        );
        root.Upd!.StackObjectsCount = 0;
        root.Upd.BuyRestrictionCurrent = 3;
        check(stock.Items.Single(i => i.Id == root.Id).Upd!.StackObjectsCount == 0, "Reading existing stock does not refill it");
        var unrelated = new Item
        {
            Id = new MongoId("000000000000000000000088"),
            Template = new MongoId(Tpl),
            Upd = new() { StackObjectsCount = 12 },
        };
        stock.Items.Add(unrelated);
        CampaignTraderStock.Restore(stock, offer, json);
        CampaignTraderStock.Restore(stock, offer, json);
        check(
            stock.Items.Count(i => i.Id == root.Id) == 1
                && stock.Items.Single(i => i.Id == root.Id).Upd!.StackObjectsCount == offer.Stock
                && unrelated.Upd.StackObjectsCount == 12,
            "Restock restores exactly one authored tree and leaves unrelated stock alone"
        );
        check(
            stock.Items.Single(i => i.Id == root.Id).Upd!.BuyRestrictionCurrent == 0 && sourceJson == JsonConvert.SerializeObject(source),
            "Native restock resets display counters without changing original imported data"
        );

        var table = JsonConvert.DeserializeObject<TemplateTable>("{}")!;
        var templates = new Dictionary<MongoId, TemplateItem>();
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(table, templates);
        templates[new MongoId(Tpl)] = json.Deserialize<TemplateItem>(
            "{\"_id\":\""
                + Tpl
                + "\",\"_type\":\"Item\",\"_parent\":\"000000000000000000000010\",\"_name\":\"Root\",\"_props\":{\"Width\":2,\"Height\":1,\"StackMaxSize\":1,\"Slots\":[{\"_name\":\"mod_scope\",\"_required\":true,\"_props\":{\"filters\":[{\"Filter\":[\""
                + ChildTpl
                + "\"]}]}}]}}"
        )!;
        templates[new MongoId(ChildTpl)] = json.Deserialize<TemplateItem>(
            "{\"_id\":\""
                + ChildTpl
                + "\",\"_type\":\"Item\",\"_parent\":\"000000000000000000000010\",\"_name\":\"Attachment\",\"_props\":{\"Width\":1,\"Height\":1,\"StackMaxSize\":1}}"
        )!;
        var locale = new LocaleService(
            null!,
            new LocaleTable
            {
                Global = new() { ["en"] = new(() => new GlobalLocaleDictionary()) },
                Menu = [],
                Languages = [],
            },
            null!
        );
        var traders = new TradersTable
        {
            [new MongoId(TraderId)] = new()
            {
                Assort = stock,
                Base = null!,
                Dialogue = [],
                QuestAssort = null!,
            },
        };
        const string circleId = "66740c3739b9da6ce402ee65",
            modCircleId = "000000000000000000000099";
        templates[new MongoId(circleId)] = json.Deserialize<TemplateItem>(
            "{\"_id\":\""
                + circleId
                + "\",\"_type\":\"Item\",\"_parent\":\"63da6da4784a55176c018dba\",\"_name\":\"CircleOfCultists_Stash_1\",\"_props\":{\"Width\":10,\"Height\":10}}"
        )!;
        templates[new MongoId(modCircleId)] = json.Deserialize<TemplateItem>(
            "{\"_id\":\""
                + modCircleId
                + "\",\"_type\":\"Item\",\"_parent\":\""
                + circleId
                + "\",\"_name\":\"Modded hideout stash\",\"_props\":{}}"
        )!;
        foreach (var template in templates.Values)
            if (template.Properties != null)
                template.Properties.Prefab = new() { Path = "test/item.bundle" };
        var itemHelper = NativeItemHelperFixture.Create(table);
        var catalogue = new TraderOfferCatalogue(table, traders, null!, locale, json, itemHelper);
        check(
            itemHelper.IsValidItem(templates[new MongoId(circleId)]),
            "Native default validity alone omits the hideout-container exclusion"
        );
        check(
            !catalogue.IsInventoryItem(circleId)
                && !catalogue.IsInventoryItem(modCircleId)
                && catalogue.Search("CircleOfCultists").Count == 0
                && catalogue.Search("Modded hideout").Count == 0,
            "Hideout storage and inherited mod variants cannot appear as merchandise"
        );
        check(
            catalogue.Search("Root").Any(i => i.Id == Tpl) && catalogue.IsInventoryItem(ChildTpl),
            "Valid installed inventory items and attachments remain available without existing trader offers"
        );
        var sceneOnlyCatalogue = new TraderOfferCatalogue(
            table,
            traders,
            null!,
            locale,
            json,
            NativeItemHelperFixture.Create(table, new MongoId(Tpl))
        );
        check(
            !sceneOnlyCatalogue.IsInventoryItem(Tpl)
                && sceneOnlyCatalogue.IsSceneItem(Tpl)
                && sceneOnlyCatalogue.SceneCatalog(new SceneCatalogRequest { Id = Tpl }).Entries.Single().Error.Length == 0,
            "Trader blacklists do not hide supported scene items"
        );
        var scenePage = catalogue.SceneCatalog(new WTT.Campaigns.Shared.Authoring.SceneCatalogRequest { Search = "Root" });
        check(scenePage.Entries.Any(e => e.Id == Tpl && e.Items.Count == 1), "Scene catalog uses installed native item templates");
        check(
            catalogue.SceneCatalog(new WTT.Campaigns.Shared.Authoring.SceneCatalogRequest { Search = "CircleOfCultists" }).Total == 0,
            "Scene catalog excludes hideout storage"
        );
        check(
            catalogue.SceneCatalog(new WTT.Campaigns.Shared.Authoring.SceneCatalogRequest { Page = 1 }).Entries.Count == 0,
            "Catalog paging does not repeat the first page"
        );
        var invalidOffer = SeasonCompiler.Copy(offer);
        const string boxId = "000000000000000000000081",
            ammoId = "000000000000000000000082";
        templates[new MongoId(ammoId)] = json.Deserialize<TemplateItem>(
            $$$"""{"_id":"{{{ammoId}}}","_type":"Item","_parent":"{{{SPTarkov.Server.Core.Models.Enums.BaseClasses.AMMO}}}","_name":"Test cartridges","_props":{"StackMaxSize":20}}"""
        )!;
        templates[new MongoId(boxId)] = json.Deserialize<TemplateItem>(
            $$$"""{"_id":"{{{boxId}}}","_type":"Item","_parent":"{{{SPTarkov.Server.Core.Models.Enums.BaseClasses.AMMO_BOX}}}","_name":"Test ammo box","_props":{"StackSlots":[{"_name":"cartridges","_max_count":25,"_props":{"filters":[{"Filter":["{{{ammoId}}}"]}]}}]}}"""
        )!;
        templates[new MongoId(ammoId)].Properties!.Prefab = new() { Path = "test/ammo.bundle" };
        templates[new MongoId(boxId)].Properties!.Prefab = new() { Path = "test/box.bundle" };
        var ammoCatalogue = new TraderOfferCatalogue(table, traders, null!, locale, json, NativeItemHelperFixture.Create(table));
        var filledBox = ammoCatalogue.SceneCatalog(new SceneCatalogRequest { Id = boxId }).Entries.Single().Items;
        var boxRoot = filledBox.Single(i => i.ParentId == null);
        var rounds = filledBox.Where(i => i.ParentId == boxRoot.Id).ToArray();
        check(
            rounds.Length == 2
                && rounds.All(i => i.Template == ammoId && i.SlotId == "cartridges")
                && rounds.Sum(i => i.Upd!.StackObjectsCount) == 25
                && rounds.All(i => i.Upd!.StackObjectsCount <= 20),
            "Scene ammo boxes contain their native ammunition capacity split into legal stacks"
        );
        check(
            ammoCatalogue.SceneCatalog(new SceneCatalogRequest { TemplateIds = new() }).Total == 0,
            "Current-map catalog with no loaded inventory items stays empty"
        );
        check(
            ammoCatalogue.SceneCatalog(new SceneCatalogRequest { TemplateIds = new() { ammoId } }).Entries.All(e => e.Id == ammoId),
            "Current-map inventory filtering happens before paging"
        );
        var nativeCapacity = templates[new MongoId(boxId)].Properties!.StackSlots!.First().MaxCount;
        templates[new MongoId(boxId)].Properties!.StackSlots!.First().MaxCount = 0;
        var brokenBox = ammoCatalogue.SceneCatalog(new SceneCatalogRequest { Id = boxId });
        check(
            brokenBox.Entries.Single().Error.Length > 0
                && brokenBox.Entries.Single().Items.Count == 0
                && ammoCatalogue.SceneCatalog(new SceneCatalogRequest { Id = ammoId }).Entries.Single().Error.Length == 0,
            "A malformed asset reports an entry error without breaking healthy catalog items"
        );
        templates[new MongoId(boxId)].Properties!.StackSlots!.First().MaxCount = nativeCapacity;
        var filledValidation = new SeasonValidationResult();
        SeasonValidator.ItemTree(filledBox, "Ammo box", filledValidation);
        check(filledValidation.CanPublish, "Filled catalog ammo boxes remain valid serializable item trees");
        var positions = rounds.Select(i => ItemStackPosition.Require(i.Location, true)).Order().ToArray();
        check(
            rounds.Any(i => i.Location == null) && positions.SequenceEqual(new[] { 0, 1 }),
            "Actual SPT ammo-box output assembles both the omitted zero position and explicit upper stack"
        );
        check(ItemStackPosition.Require(new NativeItemLocation(0), true) == 0, "Explicit ammo-box zero positions remain supported");
        Reject(() => ItemStackPosition.Require(null, false), "Missing magazine positions still fail closed");
        Reject(() => ItemStackPosition.Require(new NativeItemLocation(-1), true), "Negative ammo-box positions remain invalid");
        Reject(
            () => ItemStackPosition.Require(new NativeItemLocation(new NativeGridLocation()), true),
            "A grid location cannot be interpreted as an ammo-box stack position"
        );
        var secondBox = ammoCatalogue.SceneItem(boxId);
        check(
            !filledBox.Select(i => i.Id).Intersect(secondBox.Select(i => i.Id)).Any(),
            "Repeated ammo box placement creates fresh identities for boxes and cartridges"
        );
        templates[new MongoId(ammoId)].Properties!.StackMaxSize = 0;
        Reject(() => ammoCatalogue.SceneItem(boxId), "Invalid ammunition stack sizes fail before native filling can loop");
        templates.Remove(new MongoId(boxId));
        templates.Remove(new MongoId(ammoId));
        var blockedCatalogue = new TraderOfferCatalogue(
            table,
            traders,
            null!,
            locale,
            json,
            NativeItemHelperFixture.Create(table, new MongoId(ChildTpl))
        );
        check(
            !blockedCatalogue.IsInventoryItem(ChildTpl) && blockedCatalogue.Search("Attachment").Count == 0,
            "The catalogue respects SPT's native item blacklist"
        );
        templates[new MongoId(ChildTpl)].Properties!.QuestItem = true;
        check(!catalogue.IsInventoryItem(ChildTpl), "Native dedicated quest items are not merchandise");
        templates[new MongoId(ChildTpl)].Properties!.QuestItem = false;
        invalidOffer.Items = [new() { Id = invalidOffer.Id, Template = circleId }];
        var invalidOfferResult = new SeasonValidationResult();
        catalogue.Validate(invalidOffer, invalidOfferResult);
        check(!invalidOfferResult.CanPublish, "Imported internal storage cannot validate as a trader offer");
        AssortmentEditingChecks.Run(repository, catalogue, offer, json, check);
        var descriptor = catalogue.Item(Tpl)!;
        check(
            descriptor.Width == 2
                && descriptor.Containers.Single().Required
                && catalogue.Compatible(descriptor.Containers.Single(), ChildTpl)
                && !catalogue.Compatible(descriptor.Containers.Single(), Tpl),
            "Installed native slot metadata supplies compatibility and required-slot information"
        );
        var validation = new SeasonValidationResult();
        catalogue.Validate(offer, validation);
        check(validation.Issues.All(i => i.Severity == "dependency"), "Native catalogue accepts a compatible assembled tree");

        var service = new ItemPreviewService(repository, catalogue);
        var now = DateTimeOffset.UtcNow;
        service.UtcNow = () => now;
        var client = Guid.NewGuid().ToString("N");
        var request = new AuthoringRequest
        {
            ClientId = client,
            Enabled = true,
            Preview = new() { Fingerprint = new string('a', 64), Ready = true },
        };
        service.Exchange("owner", "character", request);
        Reject(
            () => service.Request(client, "internal-container", draft.Definition.Id, invalidOffer.Items),
            "Internal storage is rejected before a render job is queued"
        );
        check(
            service.Get(invalidOffer.Items).Status == "Unavailable" && !service.Get(invalidOffer.Items).Verified,
            "Internal storage cannot appear as cached verified merchandise"
        );
        Reject(() => service.Exchange("different-owner", "character", request), "A preview client cannot be claimed by another account");
        Reject(() => service.VerifyPublication(draft.Definition), "Unverified assemblies cannot be published");
        service.Request(client, "draft/offer", draft.Definition.Id, offer.Items);
        check(service.Get(offer.Items, draft.Definition.Id).Status == "Queued", "A requested preview enters the queue");
        var job = service.Exchange("owner", "character", request).Preview!.Job!;
        check(
            job != null && service.Get(offer.Items, draft.Definition.Id).Status == "Generating",
            "Only polling clients receive their queued job"
        );
        var wrong = SeasonCompiler.Copy(request);
        wrong.ClientId = Guid.NewGuid().ToString("N");
        wrong.Preview!.Result = new()
        {
            Id = job.Id,
            Key = job.Key,
            Verified = true,
        };
        Reject(() => service.Exchange("owner", "character", wrong), "Another client cannot complete an assigned preview");
        request.Preview!.Result = new()
        {
            Id = job.Id,
            Key = job.Key,
            Verified = true,
            Png = Convert.ToBase64String([1, 2, 3]),
        };
        Reject(() => service.Exchange("owner", "character", request), "Invalid PNG bytes do not produce a trusted receipt");
        request.Preview.Result.Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==";
        service.Exchange("owner", "character", request);
        service.Exchange("owner", "character", request);
        check(
            service.Get(offer.Items, draft.Definition.Id).Verified,
            "A matched verification result is accepted once and duplicate delivery is harmless"
        );
        service.VerifyPublication(draft.Definition);
        check(
            service.Get(offer.Items, draft.Definition.Id).HasImage && service.ImagePath(job.Key) != null,
            "Validated images are stored separately from campaign artwork"
        );
        var art = repository.AddImage(Convert.FromBase64String(request.Preview.Result.Png));
        draft.Definition.UniversalImage = draft.Definition.UniversalUnavailableImage = art;
        foreach (var document in draft.Definition.Documents)
            document.Image = document.UnavailableImage = art;
        draft = repository.Save(draft);
        offer = draft.Definition.TraderOffers.Single();
        child = offer.Items[1];
        reward = draft.Definition.SeasonalRewards.Last();
        var published = repository.Publish(draft, SeasonValidator.Validate(draft.Definition));
        var exported = repository.Export(published);
        using (var archive = new System.IO.Compression.ZipArchive(new MemoryStream(exported)))
            check(
                archive.Entries.All(e => !e.FullName.Contains("preview-cache") && !e.FullName.Contains("providers.json")),
                "Campaign exports never carry trusted verification receipts"
            );
        var imported = repository.Import(exported);
        check(
            imported.Definition.FormatVersion == 3 && imported.Definition.TraderOffers.Single().Items.Count == 2,
            "Format-3 offers survive publish, export and import"
        );
        var recovered = new ItemPreviewService(repository, catalogue);
        check(recovered.Get(offer.Items).Verified, "Locally stored verification survives an offline cache reload");
        templates[new MongoId(Tpl)].Properties!.Width = 3;
        check(!service.Get(offer.Items, draft.Definition.Id).Verified, "Changing installed template data invalidates verification");
        templates[new MongoId(Tpl)].Properties!.Width = 2;
        request.Preview.Result = null;
        request.Preview.Fingerprint = new string('b', 64);
        service.Exchange("owner", "character", request);
        service.Request(client, "draft/offer", draft.Definition.Id, offer.Items);
        check(!service.Get(offer.Items, draft.Definition.Id).Verified, "Selecting a client with changed mods requires fresh verification");
        var stale = service.Exchange("owner", "character", request).Preview!.Job!;
        child.Upd!.StackObjectsCount = 2;
        service.Request(client, "draft/offer", draft.Definition.Id, offer.Items);
        request.Preview.Result = new()
        {
            Id = stale.Id,
            Key = stale.Key,
            Verified = true,
        };
        service.Exchange("owner", "character", request);
        check(!service.Get(offer.Items, draft.Definition.Id).Verified, "Obsolete assembly results cannot verify a replacement edit");
        now = now.AddSeconds(21);
        check(service.Clients().Count == 0, "Disconnected clients expire without retaining rendering jobs");
        check(service.ImagePath("../outside") == null, "Image cache routes reject traversal keys");
        child.Upd.StackObjectsCount = 1;
        draft.Definition.SeasonalRewards.Remove(reward);
        TraderOfferAuthoring.Delete(draft.Definition, offer);
        check(draft.Definition.TraderOffers.Count == 0, "An unreferenced offer can be deleted");
    }
}
