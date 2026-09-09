using Newtonsoft.Json;
using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Hub;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using Path = System.IO.Path;

namespace SeasonalPerks.Server.Hub;

[Injectable(InjectionType.Singleton)]
public sealed partial class HubGameplay(
    SeasonService seasons,
    SaveServer saves,
    TemplateTable templates,
    TradersTable traders,
    InventoryHelper inventory,
    JsonUtil json,
    ICloner cloner,
    HubQuestService quests,
    SeasonRepository repository
)
{
    private const string StateKey = "wttSeasonalHub";
    private HubGameplayDefinition _catalogue = new();
    private HubState _presentation = new();
    private readonly Dictionary<string, TraderAssort> _offers = new();
    private readonly Dictionary<string, string> _offerIds = new();
    private bool _ready;
    private HubDocumentLoot _documentLoot = null!;
    private Dictionary<string, HubGameplay>? _runtimes;
    private HubGameplay? _manager;
    private SeasonRuntimeSnapshot _runtime = null!;

    private HubGameplay ForSession(string sessionId)
    {
        var root = seasons.ResolveRoot(sessionId);
        var profile = saves.GetProfile(new MongoId(seasons.EffectiveId(root))).CharacterData!.PmcData!;
        return _runtimes![seasons.SeasonIdFor(profile)];
    }

    public HubConfiguration Configuration { get; private set; } = new();

    internal HubProgress Progress(PmcData pmc)
    {
        var key = StateKey + ":" + _presentation.SeasonId + ":" + _presentation.Id;
        if (!pmc.ExtensionData.ContainsKey(key))
        {
            return new HubProgress { BattlePassId = _presentation.Id, SeasonId = _presentation.SeasonId };
        }
        return ProfileStateSerialization.Read<HubProgress>(pmc, key)
            ?? throw new InvalidDataException("The saved battle pass state is invalid.");
    }

    internal void Store(PmcData pmc, HubProgress state)
    {
        pmc.ExtensionData[StateKey + ":" + state.SeasonId + ":" + state.BattlePassId] = JsonConvert.SerializeObject(state);
    }

    private SptProfile Active(string root)
    {
        var id = seasons.EffectiveId(root);
        if (id == root || !seasons.IsSeasonal(id))
        {
            throw new InvalidOperationException("Open the Seasonal character to use the Battle Pass.");
        }

        var profile = saves.GetProfile(new MongoId(id));
        if (seasons.SeasonIdFor(profile.CharacterData!.PmcData!) != _presentation.SeasonId)
        {
            throw new InvalidOperationException("The selected season changed. Refresh and try again.");
        }
        return profile;
    }

    public void Initialize()
    {
        _runtimes = new();
        foreach (var runtime in repository.Playable.Values)
        {
            var handler = new HubGameplay(seasons, saves, templates, traders, inventory, json, cloner, quests, repository);
            handler._manager = this;
            handler.InitializeRuntime(runtime);
            _runtimes.Add(runtime.Definition.Id, handler);
        }
        _ready = true;
    }

    private void InitializeRuntime(SeasonRuntimeSnapshot runtime)
    {
        _runtime = runtime;
        _catalogue = runtime.Gameplay;
        _presentation = runtime.Hub;
        _documentLoot = new HubDocumentLoot(File.ReadAllText(Path.Combine(repository.ModDirectory, "data", "hub-gameplay.json")));
        var settings = runtime.Definition.Collection;
        Configuration = new HubConfiguration
        {
            DocumentsPerRaid = settings.DocumentsPerRaid,
            ClassifiedChancePercent = settings.ClassifiedChancePercent,
            MapCounts = new(settings.MapCounts, StringComparer.OrdinalIgnoreCase),
        };
        if (
            Configuration == null
            || Configuration.DocumentsPerRaid < 0
            || Configuration.DocumentsPerRaid > 8
            || Configuration.ClassifiedChancePercent < 0
            || Configuration.ClassifiedChancePercent > 100
            || Configuration.MapCounts.Values.Any(n => n < 0 || n > 8)
        )
        {
            throw new InvalidDataException("Invalid hub configuration.");
        }

        ResolveOffers();
        _ready = true;
    }

    private Dictionary<string, string> Documents
    {
        get { return _catalogue.Documents.ToDictionary(d => d.Id, d => d.ItemId); }
    }

    private string Crate
    {
        get { return (string)_catalogue.ItemExchange.ItemId; }
    }

    private HubGameplayReward Definition(string id)
    {
        return _catalogue.Rewards.GetValueOrDefault(id) ?? throw new InvalidOperationException("Unknown reward.");
    }

    private static long Now
    {
        get { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
    }

    public HubState Read(string sessionId, string seasonId = "", string characterId = "")
    {
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var effectiveId = seasons.EffectiveId(root);
        if ((characterId.Length > 0 && characterId != effectiveId) || (sessionId != root && sessionId != effectiveId))
        {
            throw new InvalidOperationException(
                "The selected character changed. Select your character again before opening the Battle Pass."
            );
        }

        var runtime = _runtimes != null ? ForSession(sessionId) : this;
        if (seasonId.Length > 0 && seasonId != runtime._presentation.SeasonId)
        {
            throw new InvalidOperationException("The selected season changed. Select your character again before opening the Battle Pass.");
        }
        return runtime.Snapshot(root);
    }

    private HubState Snapshot(string root)
    {
        if (!_ready)
        {
            throw new InvalidOperationException("Seasonal content is still loading.");
        }

        var profile = Active(root);
        var pmc = profile.CharacterData!.PmcData!;
        var state = Progress(pmc);
        var view = JsonConvert.DeserializeObject<HubState>(JsonConvert.SerializeObject(_presentation))!;
        view.PreviewOnly = false;
        view.Revision = state.Revision;
        view.UniversalCount = state.Classified;
        view.Tarcoins = state.Tarcoins;
        view.RemainingDocuments = HubRules.Remaining(state, Now, _presentation.DocumentLimit, _presentation.WindowSeconds);
        view.NextResetTime =
            state.WindowStart > 0 && Now < state.WindowStart + _presentation.WindowSeconds
                ? state.WindowStart + _presentation.WindowSeconds
                : 0;
        view.ExchangeRate = (int)_catalogue.ExchangeRate;
        view.CrateCost = (int)_catalogue.ItemExchange.RequiredDocuments;
        var owned = Balances(pmc);
        foreach (var doc in view.Documents)
        {
            doc.Count = owned[doc.Id];
        }

        view.ExchangeUnavailableReason = Documents.Values.Any(t => !templates.Items.ContainsKey(new MongoId(t)))
            ? "Seasonal document assets are not installed."
            : "";
        view.CrateUnavailableReason = ItemUnavailable(Crate);
        if (view.CrateUnavailableReason.Length == 0)
        {
            view.CrateUnavailableReason = view.ExchangeUnavailableReason;
        }
        foreach (var page in view.Pages)
        {
            page.ClaimedCount = page.Rewards.Count(r => state.Claimed.Contains(r.Id));
        }

        foreach (var reward in view.Pages.SelectMany(p => p.Rewards).Concat(view.SeasonalRewards))
        {
            reward.Claimed = state.Claimed.Contains(reward.Id);
            reward.Eligibility = Definition(reward.Id)
                .Conditions.Select(condition =>
                {
                    var kind = (string)condition.ConditionType!;
                    var target = (string?)condition.Target ?? "";
                    var required = kind == "Level" ? (int)condition.Value! : 1;
                    var current =
                        kind == "Level" ? pmc.Info!.Level ?? 0
                        : pmc.Quests!.Any(q => q.QId.ToString() == target && (int)q.Status == 4) ? 1
                        : 0;
                    var unavailable = kind == "Quest" ? quests.UnavailableReason(target) : "";
                    return new HubRequirement
                    {
                        Kind = kind,
                        Target = target,
                        Required = required,
                        Current = current,
                        Met = current >= required && unavailable.Length == 0,
                        UnavailableReason = unavailable,
                    };
                })
                .ToArray();
            reward.UniversalNeeded = HubRules.Shortage(
                reward.Costs.Select(c => new KeyValuePair<string, int>(c.DocumentId, c.Count)),
                owned
            );
            reward.UnavailableReason = Eligibility(reward, pmc, state);
            reward.CanClaim = reward.UnavailableReason.Length == 0 && reward.UniversalNeeded <= state.Classified;
            if (reward.UnavailableReason.Length == 0 && !reward.CanClaim)
            {
                reward.UnavailableReason = "Collect more documents to claim this reward.";
            }
        }
        view.ClaimedRewards = view.Pages.Sum(p => p.ClaimedCount);
        return view;
    }

    private Dictionary<string, int> Balances(PmcData pmc)
    {
        return Documents.ToDictionary(d => d.Key, d => checked((int)Spendable(pmc, d.Value).Sum(i => i.Upd?.StackObjectsCount ?? 1)));
    }

    // Only normal inventory ancestry is spendable. Quest inventory and detached items never qualify.
    private static IEnumerable<Item> Spendable(PmcData pmc, string tpl)
    {
        var items = pmc.Inventory!.Items!;
        var byId = items.ToDictionary(i => i.Id.ToString());
        return items.Where(i => i.Template.ToString() == tpl && Rooted(i));
        bool Rooted(Item item)
        {
            var seen = new HashSet<string>();
            while (item.ParentId != null && seen.Add(item.Id.ToString()))
            {
                if (item.ParentId == pmc.Inventory.Stash.ToString() || item.ParentId == pmc.Inventory.Equipment.ToString())
                {
                    return true;
                }

                if (!byId.TryGetValue(item.ParentId, out item!))
                {
                    return false;
                }
            }
            return false;
        }
    }

    private string Eligibility(HubReward reward, PmcData pmc, HubProgress state)
    {
        if (!string.IsNullOrEmpty(reward.Side) && !string.Equals(reward.Side, pmc.Info?.Side, StringComparison.OrdinalIgnoreCase))
        {
            return "This reward is for the other faction.";
        }

        if (state.Claimed.Contains(reward.Id))
        {
            return "This reward has already been claimed.";
        }

        for (var page = 1; page < _presentation.Pages.Length; page++)
        {
            if (
                _presentation.Pages[page].Rewards.Any(r => r.Id == reward.Id)
                && _presentation.Pages[page - 1].Rewards.Count(r => state.Claimed.Contains(r.Id))
                    < _presentation.Pages[page].PreviousRequirement
            )
            {
                return "Claim more rewards from the previous page.";
            }
        }

        foreach (var condition in Definition(reward.Id).Conditions)
        {
            var kind = (string?)condition.ConditionType;
            if (kind == "Level" && pmc.Info!.Level < (int)condition.Value!)
            {
                return "Reach level " + condition.Value + ".";
            }

            if (kind == "Quest")
            {
                var id = (string)condition.Target!;
                var unavailable = quests.UnavailableReason(id);
                if (unavailable.Length > 0)
                {
                    return unavailable;
                }

                if (!pmc.Quests!.Any(q => q.QId.ToString() == id && (int)q.Status == 4))
                {
                    return "Complete the required seasonal task.";
                }
            }
            if (kind is not ("Quest" or "Level"))
            {
                return "This reward requires an unsupported condition.";
            }
        }
        foreach (var grant in Definition(reward.Id).Grants)
        {
            var reason = GrantUnavailable(grant);
            if (reason.Length > 0)
            {
                return reason;
            }
        }
        return "";
    }

    private string GrantUnavailable(NativeReward grant)
    {
        var kind = (string)grant.Type!;
        if (kind == "Tarcoin")
        {
            return "";
        }

        if (kind == "CustomizationDirect")
        {
            var id = new MongoId((string)grant.Target!);
            if (!templates.Customization.TryGetValue(id, out var custom) || CustomizationKind(custom.Parent) == null)
            {
                return "Required customization is not registered or supported: " + id;
            }

            return "";
        }
        if (kind is not ("Item" or "AssortmentUnlock"))
        {
            return "Unsupported reward type: " + kind;
        }

        foreach (var item in grant.Items!)
        {
            var reason = ItemUnavailable((string)item.Template!);
            if (reason.Length > 0)
            {
                return reason;
            }
        }

        if (kind == "AssortmentUnlock" && !_offers.ContainsKey((string)grant.Target!))
        {
            return "The required trader offer is unavailable.";
        }

        return "";
    }

    private string ItemUnavailable(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return "Crate exchange is disabled for this season.";
        }

        var id = new MongoId(template);
        if (!templates.Items.ContainsKey(id))
        {
            return "Required item is not registered: " + template;
        }
        if (
            (
                template is "6a3567f687d90a0deb066c1b" or "6a4fa628b4831242f306e8cd"
                || _runtime.Definition.Crates.Any(c => c.ItemId == template)
            )
            && inventory.GetRandomLootContainerRewardDetails(id) == null
        )
        {
            return "The season crate contents have not been recovered.";
        }
        return "";
    }

    public async Task<HubResult> Transact(string sessionId, HubRequest request, string action)
    {
        if (_runtimes != null)
        {
            return await ForSession(sessionId).Transact(sessionId, request, action);
        }

        ValidateSeasonRequest(request);
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var original = Active(root);
        var id = new MongoId(seasons.EffectiveId(root));
        seasons.EnsureNotInRaid(id.ToString());
        if (!Guid.TryParseExact(request.OperationId, "N", out _))
        {
            throw new InvalidOperationException("Invalid operation identifier.");
        }

        var state = Progress(original.CharacterData!.PmcData!);
        var fingerprint = JsonConvert.SerializeObject(
            new
            {
                action,
                request.RewardId,
                request.DocumentId,
                request.Crate,
                request.UseClassified,
                Sources = request.Sources.OrderBy(p => p.Key).ToArray(),
            }
        );
        if (state.Receipts.TryGetValue(request.OperationId, out var receipt))
        {
            if (receipt.Fingerprint != fingerprint)
            {
                throw new InvalidOperationException("Operation identifier was reused with different inputs.");
            }

            return new HubResult
            {
                OperationId = request.OperationId,
                Committed = true,
                Message = receipt.Message,
                State = Snapshot(root),
            };
        }
        if (state.Revision != request.ExpectedRevision)
        {
            throw new InvalidOperationException("The hub changed. Refresh and try again.");
        }

        var staged = cloner.Clone(original)!;
        var pmc = staged.CharacterData!.PmcData!;
        var message = action == "claim" ? Claim(staged, state, request) : Exchange(staged, state, request);
        state.Revision++;
        state.Receipts.Add(
            request.OperationId,
            new HubReceipt
            {
                Fingerprint = fingerprint,
                Revision = state.Revision,
                Message = message,
            }
        );
        Store(pmc, state);
        await Commit(id, original, staged);
        return new HubResult
        {
            OperationId = request.OperationId,
            Committed = true,
            Message = message,
            State = Snapshot(root),
        };
    }

    internal async Task Commit(MongoId id, SptProfile original, SptProfile staged)
    {
        if (saves.IsProfileInvalidOrUnloadable(id))
        {
            throw new InvalidOperationException("The Seasonal profile cannot be saved.");
        }
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
            catch
            { /* Preserve the original write failure. */
            }
            throw;
        }
    }

    internal void ValidateSeasonRequest(HubRequest request)
    {
        if (!_runtime.Definition.Legacy && request.ProtocolVersion != 2)
        {
            throw new InvalidOperationException("Update the Seasonal client and server together.");
        }

        if (request.SeasonId.Length > 0 && request.SeasonId != _presentation.SeasonId)
        {
            throw new InvalidOperationException("This operation belongs to another season.");
        }
    }

    private string Claim(SptProfile profile, HubProgress state, HubRequest request)
    {
        var reward =
            _presentation
                .Pages.SelectMany(p => p.Rewards)
                .Concat(_presentation.SeasonalRewards)
                .SingleOrDefault(r => r.Id == request.RewardId)
            ?? throw new InvalidOperationException("Unknown reward.");
        var pmc = profile.CharacterData!.PmcData!;
        var reason = Eligibility(reward, pmc, state);
        if (reason.Length > 0)
        {
            throw new InvalidOperationException(reason);
        }

        var owned = Balances(pmc);
        var shortage = HubRules.Shortage(reward.Costs.Select(c => new KeyValuePair<string, int>(c.DocumentId, c.Count)), owned);
        if (shortage > state.Classified || shortage > 0 && !request.UseClassified)
        {
            throw new InvalidOperationException("Confirm Classified document use or collect the missing documents.");
        }

        foreach (var cost in reward.Costs)
        {
            Consume(pmc, Documents[cost.DocumentId], Math.Min(cost.Count, owned[cost.DocumentId]));
        }

        state.Classified -= shortage;
        foreach (var grant in Definition(reward.Id).Grants)
        {
            Grant(profile, state, grant);
        }

        state.Claimed.Add(reward.Id);
        return "Claimed " + reward.Name + ".";
    }

    private string Exchange(SptProfile profile, HubProgress state, HubRequest request)
    {
        if (request.Sources.Count == 0 || request.Sources.Any(p => p.Value <= 0 || !Documents.ContainsKey(p.Key)))
        {
            throw new InvalidOperationException("Select ordinary documents to exchange.");
        }

        var cost = request.Crate ? (int)_catalogue.ItemExchange.RequiredDocuments : (int)_catalogue.ExchangeRate;
        if (request.Sources.Values.Sum(v => (long)v) != cost)
        {
            throw new InvalidOperationException("Incorrect document exchange quantity.");
        }

        var tpl = request.Crate
            ? Crate
            : Documents.GetValueOrDefault(request.DocumentId) ?? throw new InvalidOperationException("Unknown document.");
        var unavailable = ItemUnavailable(tpl);
        if (unavailable.Length > 0)
        {
            throw new InvalidOperationException(unavailable);
        }

        var pmc = profile.CharacterData!.PmcData!;
        foreach (var source in request.Sources)
        {
            Consume(pmc, Documents[source.Key], source.Value);
        }

        AddItems(
            profile,
            [
                new Item
                {
                    Id = new MongoId(),
                    Template = new MongoId(tpl),
                    Upd = new Upd { StackObjectsCount = 1 },
                },
            ]
        );
        return request.Crate ? "Gear crate added to your stash." : "Document added to your stash.";
    }

    private static void Consume(PmcData pmc, string tpl, int count)
    {
        var stacks = Spendable(pmc, tpl).OrderBy(i => i.Id.ToString(), StringComparer.Ordinal).ToList();
        if (stacks.Sum(i => i.Upd?.StackObjectsCount ?? 1) < count)
        {
            throw new InvalidOperationException("Documents changed. Refresh and try again.");
        }

        foreach (var stack in stacks)
        {
            if (count == 0)
            {
                break;
            }

            var take = (int)Math.Min(count, stack.Upd?.StackObjectsCount ?? 1);
            stack.Upd ??= new Upd { StackObjectsCount = 1 };
            stack.Upd.StackObjectsCount = (stack.Upd.StackObjectsCount ?? 1) - take;
            count -= take;
            if (stack.Upd.StackObjectsCount == 0)
            {
                pmc.Inventory!.Items!.Remove(stack);
            }
        }
    }

    private void Grant(SptProfile profile, HubProgress state, NativeReward grant)
    {
        var target = (string?)grant.Target ?? "";
        switch ((string)grant.Type!)
        {
            case "Tarcoin":
                state.Tarcoins = checked(state.Tarcoins + (long)grant.Value!);
                break;
            case "AssortmentUnlock":
                state.UnlockedOffers.Add(target);
                break;
            case "CustomizationDirect":
                profile.CustomisationUnlocks ??= [];
                if (!profile.CustomisationUnlocks.Any(c => c.Id.ToString() == target))
                {
                    profile.CustomisationUnlocks.Add(
                        new CustomisationStorage
                        {
                            Id = new MongoId(target),
                            Source = CustomisationSource.UNLOCKED_IN_GAME,
                            Type = CustomizationKind(templates.Customization[new MongoId(target)].Parent)!,
                        }
                    );
                }

                break;
            case "Item":
                var items = json.Deserialize<List<Item>>(JsonConvert.SerializeObject(grant.Items))!;
                AddItems(profile, items);
                break;
            default:
                throw new InvalidOperationException("Unsupported reward.");
        }
    }

    private void AddItems(SptProfile profile, List<Item> items)
    {
        var pmc = profile.CharacterData!.PmcData!;
        var id = profile.ProfileInfo!.ProfileId!.Value;
        var ids = items.ToDictionary(i => i.Id.ToString(), _ => new MongoId().ToString());
        foreach (var item in items)
        {
            var old = item.Id.ToString();
            item.Id = new MongoId(ids[old]);
            if (item.ParentId != null && ids.TryGetValue(item.ParentId, out var parent))
            {
                item.ParentId = parent;
            }

            item.Upd ??= new Upd { StackObjectsCount = 1 };
        }
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
        // AddItemToStash uses the staged profile's grid and never the live profile cache.
        inventory.AddItemToStash(
            id,
            new AddItemDirectRequest
            {
                ItemWithModsToAdd = items,
                FoundInRaid = false,
                UseSortingTable = false,
            },
            pmc,
            output
        );
        if (output.Warnings?.Count > 0 || !pmc.Inventory!.Items!.Any(i => i.Id == items[0].Id))
        {
            throw new InvalidOperationException("Make room in your stash before claiming this reward.");
        }
    }

    internal static string? CustomizationKind(string parent)
    {
        return parent switch
        {
            CustomisationTypeId.UPPER or CustomisationTypeId.LOWER => CustomisationType.SUITE,
            CustomisationTypeId.HEAD => CustomisationType.HEAD,
            CustomisationTypeId.VOICE => CustomisationType.VOICE,
            CustomisationTypeId.DOG_TAGS => CustomisationType.DOG_TAG,
            CustomisationTypeId.FLOOR => CustomisationType.FLOOR,
            CustomisationTypeId.CEILING => CustomisationType.CEILING,
            CustomisationTypeId.WALL => CustomisationType.WALL,
            CustomisationTypeId.ENVIRONMENT_UI => CustomisationType.ENVIRONMENT,
            CustomisationTypeId.SHOOTING_RANGE_MARK => CustomisationType.SHOOTING_RANGE_MARK,
            CustomisationTypeId.MANNEQUIN_POSE => CustomisationType.MANNEQUIN_POSE,
            _ => null,
        };
    }
}
