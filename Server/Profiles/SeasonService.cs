using System.Collections.Concurrent;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Modding;
using SPTarkov.Server.Core.Services.Profile;
using WTT.Campaigns.Server.Effects;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Configuration;
using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Effects;
using WTT.Campaigns.Shared.Effects.Consumables;
using WTT.Campaigns.Shared.Perks;
using WTT.Campaigns.Shared.Profiles;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Profiles;

[Injectable(InjectionType.Singleton)]
public sealed class SeasonService(
    SaveServer saves,
    ProfileDataService profileData,
    CreateProfileService creator,
    TemplateTable templates,
    SeasonRepository repository,
    SeasonStartingService starting
)
{
    private const string StateKey = "wttCampaignsState";

    private static string StoryChapters(PmcData? pmc, WTT.Campaigns.Shared.Story.StoryDefinition? story)
    {
        if (pmc == null || story == null)
        {
            return "";
        }
        var facts = new WTT.Campaigns.Shared.Story.StoryFacts
        {
            QuestStatuses = (pmc.Quests ?? []).ToDictionary(q => q.QId.ToString(), q => q.Status.ToString()),
        };
        return story.Chapters.Count(c => WTT.Campaigns.Shared.Story.StoryRules.ChapterComplete(c, story, facts))
            + "/"
            + story.Chapters.Count;
    }

    private const string LinkKey = "wttCampaignsAccount";
    private readonly ConcurrentDictionary<string, AccountLink> _links = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly Dictionary<string, Catalogue> _catalogues = new();
    public Catalogue Catalogue { get; private set; } = new();
    public Dictionary<string, string> Locale { get; private set; } = new();
    public Rules Rules { get; private set; } = new();
    public Dictionary<string, string> Unavailable { get; } = new();

    public void Initialize()
    {
        foreach (var runtime in repository.Playable.Values)
        {
            var season = runtime.Definition;
            _catalogues[season.Id] = season.Perks;
        }
        var definition = repository.Current.Definition;
        foreach (var profile in saves.GetProfiles().Values)
        {
            var pmc = profile.CharacterData?.PmcData;
            if (pmc != null)
            {
                var state = State(pmc);
                if (state.Revision > 0 && repository.Playable.TryGetValue(state.SeasonId ?? SeasonRepository.LegacyId, out var runtime))
                {
                    repository.MarkUsed(runtime.Definition);
                }
            }
        }
        Catalogue = definition.Perks;
        Locale = definition.Locales["en"];
        Rules = definition.Rules;
        foreach (var p in Catalogue.All)
        {
            var reason =
                EffectSupport.UnavailableReason(p) ?? p.Effects.Select(EffectParametersValidator.Error).FirstOrDefault(e => e != null);
            if (reason != null)
            {
                Unavailable[p.Id] = reason;
            }
        }
        if (Rules.EnabledCommonIds.Any(id => Unavailable.ContainsKey(id) || !Catalogue.Common.Any(p => p.Id == id)))
        {
            throw new InvalidDataException("EnabledCommonIds contains an unsupported or unknown global modifier.");
        }
    }

    public IDisposable Enter(string root)
    {
        var gate = _gates.GetOrAdd(root, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Character operation is still in progress.");
        }

        return new Lease(gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }
        }
    }

    private AccountLink Link(string root)
    {
        var link = _links.GetOrAdd(
            root,
            id => profileData.GetProfileDataAsync<AccountLink>(new MongoId(id), LinkKey).GetAwaiter().GetResult() ?? new AccountLink()
        );
        var changed = false;
        void Migrate(string id, string seasonId, bool created)
        {
            if (link.Characters.Any(c => c.ProfileId == id))
            {
                return;
            }

            link.Characters.Add(
                new SeasonCharacterLink
                {
                    ProfileId = id,
                    SeasonId = seasonId,
                    Created = created,
                }
            );
            changed = true;
        }
        foreach (var old in link.Seasons)
        {
            Migrate(old.Value.ProfileId, old.Key, old.Value.Created);
        }

        if (link.SeasonalId != null)
        {
            Migrate(link.SeasonalId, link.CurrentSeasonId ?? SeasonRepository.LegacyId, link.Created);
        }

        if (link.Mode == "seasonal" && !repository.Playable.ContainsKey(link.CurrentSeasonId ?? SeasonRepository.LegacyId))
        {
            link.Mode = "normal";
            changed = true;
        }
        if (changed)
        {
            profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link).GetAwaiter().GetResult();
        }

        return link;
    }

    public string EffectiveId(string root)
    {
        var link = Link(root);
        return link.Mode == "seasonal" && link.Created ? link.SeasonalId! : root;
    }

    public bool IsSeasonal(string id)
    {
        var state = State(saves.GetProfile(new MongoId(id)).CharacterData!.PmcData!);
        return state.Revision > 0 && repository.Playable.ContainsKey(state.SeasonId ?? SeasonRepository.LegacyId);
    }

    public string CharacterSeasonId(string id)
    {
        var state = State(saves.GetProfile(new MongoId(id)).CharacterData!.PmcData!);
        return state.Revision > 0 ? state.SeasonId ?? SeasonRepository.LegacyId : "";
    }

    public static PerkState State(PmcData pmc)
    {
        return ProfileStateSerialization.Read<PerkState>(pmc, StateKey) ?? new PerkState();
    }

    private static void SetState(PmcData pmc, PerkState state)
    {
        // Keep the persisted state contract independent of SPT's System.Text.Json serializer.
        // State and creation receipts are committed in the SAME profile save as the grant.
        pmc.ExtensionData[StateKey] = JsonConvert.SerializeObject(state);
    }

    public string SeasonIdFor(PmcData pmc)
    {
        var state = State(pmc);
        return state.Revision > 0 ? state.SeasonId ?? SeasonRepository.LegacyId : repository.Current.Definition.Id;
    }

    public RuntimeEffects Effects(PmcData pmc)
    {
        var state = State(pmc);
        var catalogue = _catalogues.GetValueOrDefault(state.SeasonId ?? SeasonRepository.LegacyId) ?? Catalogue;
        return new RuntimeEffects(catalogue, state.Revision > 0 ? state.SeasonalPerks : []);
    }

    public ServerSnapshot GetSnapshot(string root, string seasonId = "", string characterId = "")
    {
        var link = Link(root);
        var selected = link.Characters.FirstOrDefault(c => c.ProfileId == (characterId.Length > 0 ? characterId : link.SeasonalId));
        var requestedSeason = seasonId.Length > 0 ? seasonId : selected?.SeasonId ?? "";
        if (seasonId.Length == 0 && !repository.Playable.ContainsKey(requestedSeason))
        {
            requestedSeason = "";
        }

        var runtime = repository.Runtime(requestedSeason);
        var definition = runtime.Definition;
        var seasonal =
            selected?.Created == true && selected.SeasonId == definition.Id
                ? saves.GetProfile(new MongoId(selected.ProfileId)).CharacterData!.PmcData
                : null;
        var snapshot = new ServerSnapshot
        {
            ProtocolVersion = 2,
            SeasonId = definition.Id,
            SeasonName = definition.Name,
            Seasons = repository
                .Playable.Values.Select(r => r.Definition)
                .OrderBy(d => d.Name)
                .Select(d => new SeasonChoice
                {
                    Id = d.Id,
                    Name = d.Name,
                    Description = d.Description,
                })
                .ToList(),
            SelectedCharacterId = seasonal == null ? "" : selected!.ProfileId,
            PackRevision = definition.Revision,
            BannerImage = definition.Branding.Banner,
            LegacyBranding = definition.Legacy,
            DocumentTemplates = definition.Documents.Select(d => d.ItemId).ToList(),
            Catalogue = definition.Perks,
            Locale = definition.Locales["en"],
            Unavailable = Unsupported(definition.Perks),
            Rules = definition.Rules,
            HasStory = definition.Story != null,
            Zones = definition.Zones,
            ActiveMode = link.Mode,
            EffectiveProfileId = EffectiveId(root),
            State = seasonal == null ? new PerkState() : State(seasonal),
        };
        void Add(string id, string mode, string season, bool created)
        {
            var profile = created && saves.ProfileExists(new MongoId(id)) ? saves.GetProfile(new MongoId(id)) : null;
            var pmc = ProfileReadiness.PlayablePmc(profile);
            var entry = link.Characters.FirstOrDefault(c => c.ProfileId == id);
            var name = repository.Playable.TryGetValue(season, out var pack) ? pack.Definition.Name : season;
            snapshot.Characters.Add(
                new()
                {
                    Id = id,
                    Mode = mode,
                    SeasonId = season,
                    SeasonName = name,
                    CreationOperationId = link.Characters.FirstOrDefault(c => c.ProfileId == id)?.CreationOperationId ?? "",
                    Available = mode == "normal" || repository.Playable.ContainsKey(season),
                    Name = pmc?.Info?.Nickname ?? entry?.Name ?? (mode == "normal" ? "Main character" : "Wiped character"),
                    Wiped = entry?.Wiped == true,
                    Level = pmc?.Info?.Level ?? 1,
                    StoryChapters = StoryChapters(pmc, pack?.Definition.Story),
                    Exists = pmc?.Info != null,
                    Side = pmc?.Info?.Side ?? "Usec",
                    Visual = pmc?.Info == null ? null : Visual(pmc),
                }
            );
        }
        Add(root, "normal", "", true);
        foreach (var character in link.Characters.Where(c => c.Created || c.Wiped))
        {
            Add(character.ProfileId, "seasonal", character.SeasonId, character.Created);
        }

        foreach (var perk in snapshot.Catalogue.All)
        {
            perk.ImageUrl = "/wtt-campaigns/icons/" + perk.Id + ".png";
        }

        return snapshot;
    }

    private static Dictionary<string, string> Unsupported(Catalogue catalogue)
    {
        var result = new Dictionary<string, string>();
        foreach (var perk in catalogue.All)
        {
            var reason =
                EffectSupport.UnavailableReason(perk)
                ?? perk.Effects.Select(EffectParametersValidator.Error).FirstOrDefault(e => e != null);
            if (reason != null)
            {
                result[perk.Id] = reason;
            }
        }
        return result;
    }

    private CharacterVisual? Visual(PmcData profile)
    {
        if (profile.Inventory?.Items == null || profile.Inventory.Equipment == null || profile.Info == null)
        {
            return null;
        }
        var inventory = profile.Inventory!;
        var ids = new HashSet<string> { inventory.Equipment.ToString()! };
        var items = inventory.Items!.ToArray();
        bool added;
        do
        {
            added = false;
            foreach (var item in items)
            {
                if (item.ParentId != null && ids.Contains(item.ParentId))
                {
                    added |= ids.Add(item.Id.ToString());
                }
            }
        } while (added);
        // Only the visible character contract leaves the server. Stash and progression are excluded.
        return new CharacterVisual
        {
            Info = new CharacterVisualInfo
            {
                Nickname = profile.Info.Nickname,
                Level = profile.Info.Level,
                Side = profile.Info.Side,
            },
            Customization = profile.Customization,
            Equipment = new CharacterEquipment
            {
                Id = inventory.Equipment,
                Items = items.Where(item => ids.Contains(item.Id.ToString())).ToArray(),
            },
        };
    }

    public RuntimeEffects Effects(string effectiveId)
    {
        var pmc = saves.GetProfile(new MongoId(effectiveId)).CharacterData!.PmcData!;
        return Effects(pmc);
    }

    public async Task<ServerSnapshot> Create(string root, Mutation request)
    {
        if (request.PerkIds == null)
        {
            throw new InvalidOperationException("A personal perk selection is required.");
        }
        var link = Link(root);
        var definition = repository.Runtime(request.SeasonId).Definition;
        var entry =
            request.CharacterId.Length > 0
                ? link.Characters.FirstOrDefault(c => c.ProfileId == request.CharacterId)
                    ?? throw new InvalidOperationException("This character does not belong to this account.")
            : request.OperationId.Length > 0 ? link.Characters.FirstOrDefault(c => c.CreationOperationId == request.OperationId)
            : link.Characters.FirstOrDefault(c =>
                !c.Created && !c.Wiped && c.SeasonId == definition.Id && c.CreationOperationId.Length == 0
            );
        if (entry?.Created == true && entry.CreationOperationId != request.OperationId)
        {
            throw new InvalidOperationException("Wipe this character before creating it again.");
        }
        var restartRecreation = entry?.Wiped == true && !entry.Created && entry.CreationOperationId != request.OperationId;

        var fingerprint = JsonConvert.SerializeObject(
            new
            {
                definition.Id,
                request.Side,
                Nickname = CleanNickname(request.Nickname),
                request.HeadId,
                request.VoiceId,
                Perks = request.PerkIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            }
        );
        if (!restartRecreation && entry != null && entry.CreationFingerprint.Length > 0 && entry.CreationFingerprint != fingerprint)
        {
            throw new InvalidOperationException(
                "This creation request was already used with different choices. Start a new character draft."
            );
        }
        if (entry != null && entry.SeasonId != definition.Id)
        {
            throw new InvalidOperationException("The creation request belongs to another campaign.");
        }

        if (entry?.Created == true)
        {
            return GetSnapshot(root, entry.SeasonId, entry.ProfileId);
        }

        if (request.OperationId.Length == 0 && link.Characters.Any(c => c.Created && c.SeasonId == definition.Id))
        {
            throw new InvalidOperationException("A creation operation ID is required for an additional character.");
        }

        if (request.Side is not ("Usec" or "Bear"))
        {
            throw new InvalidOperationException("Invalid faction.");
        }

        Validate(request, definition);
        var headId = CreationCustomization(request.Side, "5cc085e214c02e000c6bea67", request.HeadId);
        var voiceId = CreationCustomization(request.Side, "5fc100cf95572123ae738483", request.VoiceId);
        var account = saves.GetProfile(new MongoId(root));
        EnsureNotInRaid(EffectiveId(root));
        if (entry == null)
        {
            entry = new SeasonCharacterLink
            {
                ProfileId = NewId(),
                SeasonId = definition.Id,
                CreationOperationId = request.OperationId,
                CreationFingerprint = fingerprint,
            };
            link.Characters.Add(entry);
        }
        if (entry.Wiped && (entry.CreationOperationId.Length == 0 || restartRecreation))
        {
            // The achievement receipt lives in the account link until recreation is complete.
            // Clear a file left by an interrupted wipe before reserving the creation request.
            RemoveRetiredProfile(entry.ProfileId);
            entry.CreationOperationId = request.OperationId;
            entry.CreationFingerprint = fingerprint;
        }
        await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        var id = new MongoId(entry.ProfileId);
        SeasonProfileStorage.Register(entry.ProfileId);
        if (!saves.ProfileExists(id))
        {
            saves.CreateProfile(
                new Info
                {
                    ProfileId = id,
                    ScavengerId = new MongoId(NewId()),
                    Aid = saves.GetProfiles().Values.Max(p => p.ProfileInfo?.Aid ?? 0) + 1,
                    Username = (account.ProfileInfo!.Username ?? "PMC") + "-seasonal",
                    Edition =
                        entry.Wiped || string.IsNullOrEmpty(definition.Starting.Preset)
                            ? account.ProfileInfo.Edition
                            : definition.Starting.Preset,
                    IsWiped = false,
                }
            );
        }

        var existing = saves.GetProfile(id).CharacterData!.PmcData!;
        if (existing.Info == null)
        {
            if (entry.Wiped)
            {
                existing.Achievements = entry.PreservedAchievements.ToDictionary(p => new MongoId(p.Key), p => p.Value);
            }
            // Use SPT's own starter profile and faction-specific cosmetics, never clone player progression.
            await creator.CreateProfile(
                id,
                new ProfileCreateRequestData
                {
                    Side = request.Side,
                    Nickname = CleanNickname(request.Nickname),
                    HeadId = headId,
                    VoiceId = voiceId,
                }
            );
        }
        repository.CheckGameplay(definition);
        await starting.Apply(id, request.Side, definition.Id);
        // Resume a creation interrupted after its profile/receipts were committed.
        if (State(saves.GetProfile(id).CharacterData!.PmcData!).Revision == 0)
        {
            await ApplySelection(id, request.PerkIds, 0, root, definition.Id);
        }

        repository.MarkUsed(definition);
        var before = SeasonCompiler.Copy(link);
        entry.Created = true;
        entry.Wiped = false;
        entry.PreservedAchievements.Clear();
        if (link.Mode == "normal")
        {
            SelectLink(link, entry);
        }

        try
        {
            await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        }
        catch
        {
            _links[root] = before;
            throw;
        }
        return GetSnapshot(root, definition.Id, entry.ProfileId);
    }

    private static void SelectLink(AccountLink link, SeasonCharacterLink entry)
    {
        link.SeasonalId = entry.ProfileId;
        link.CurrentSeasonId = entry.SeasonId;
        link.Created = entry.Created;
    }

    private static SeasonCharacterLink Owned(AccountLink link, string id)
    {
        return link.Characters.FirstOrDefault(c => c.ProfileId == id && c.Created)
            ?? throw new InvalidOperationException("This character does not belong to this account.");
    }

    public async Task<ServerSnapshot> Edit(string root, Mutation request)
    {
        var link = Link(root);
        var entry = Owned(link, request.CharacterId.Length > 0 ? request.CharacterId : link.SeasonalId ?? "");
        var definition = repository.Runtime(entry.SeasonId).Definition;
        if (request.SeasonId.Length > 0 && request.SeasonId != entry.SeasonId)
        {
            throw new InvalidOperationException("These modifiers belong to a different campaign.");
        }
        if (!definition.Rules.AllowEdits)
        {
            throw new InvalidOperationException("Perk editing is disabled in this campaign.");
        }

        EnsureNotInRaid(EffectiveId(root));
        Validate(request, definition);
        await ApplySelection(new MongoId(entry.ProfileId), request.PerkIds, request.ExpectedRevision);
        return GetSnapshot(root, entry.SeasonId, entry.ProfileId);
    }

    public async Task<ServerSnapshot> Switch(string root, string mode, string characterId = "")
    {
        var link = Link(root);
        if (mode is not ("normal" or "seasonal"))
        {
            throw new InvalidOperationException("Unknown character mode.");
        }

        var entry = mode == "seasonal" ? Owned(link, characterId.Length > 0 ? characterId : link.SeasonalId ?? "") : null;
        if (entry != null)
        {
            repository.Runtime(entry.SeasonId);
        }

        var current = EffectiveId(root);
        EnsureNotInRaid(current);
        await saves.SaveProfileAsync(new MongoId(current));
        var before = SeasonCompiler.Copy(link);
        if (entry != null)
        {
            SelectLink(link, entry);
        }

        link.Mode = mode;
        try
        {
            await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        }
        catch
        {
            _links[root] = before;
            throw;
        }
        return GetSnapshot(root);
    }

    public async Task<ServerSnapshot> Delete(string root, Mutation request)
    {
        var link = Link(root);
        if (link.RetiredCharacters.ContainsKey(request.CharacterId) && !link.Characters.Any(c => c.ProfileId == request.CharacterId))
        {
            RemoveRetiredProfile(request.CharacterId);
            return GetSnapshot(root);
        }
        var entry =
            link.Characters.FirstOrDefault(c => c.ProfileId == request.CharacterId && (c.Created || c.Wiped))
            ?? throw new InvalidOperationException("This character does not belong to this account.");
        EnsureNotInRaid(EffectiveId(root));
        if (saves.ProfileExists(new MongoId(entry.ProfileId)))
        {
            EnsureNotInRaid(entry.ProfileId);
        }

        if (EffectiveId(root) == entry.ProfileId)
        {
            throw new InvalidOperationException("Switch to another character before deleting or wiping this one.");
        }

        var before = SeasonCompiler.Copy(link);
        link.Characters.Remove(entry);
        link.RetiredCharacters.TryAdd(entry.ProfileId, "");
        foreach (var old in link.Seasons.Where(p => p.Value.ProfileId == entry.ProfileId).Select(p => p.Key).ToArray())
        {
            link.Seasons.Remove(old);
        }

        if (link.SeasonalId == entry.ProfileId)
        {
            link.SeasonalId = null;
            link.Created = false;
            link.CurrentSeasonId = null;
        }
        try
        {
            await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        }
        catch
        {
            _links[root] = before;
            throw;
        }
        RemoveRetiredProfile(entry.ProfileId);
        return GetSnapshot(root);
    }

    private void RemoveRetiredProfile(string profileId)
    {
        var id = new MongoId(profileId);
        if (!saves.ProfileExists(id))
        {
            return;
        }

        var original = saves.GetProfile(id);
        if (!saves.RemoveProfile(id))
        {
            saves.AddProfile(original);
            throw new InvalidOperationException(
                "Character removed from selection, but SPT could not remove its saved file. Retry deletion."
            );
        }
    }

    public async Task<ServerSnapshot> Wipe(string root, Mutation request)
    {
        if (string.IsNullOrEmpty(request.OperationId))
        {
            throw new InvalidOperationException("A wipe operation ID is required.");
        }

        var link = Link(root);
        var entry =
            link.Characters.FirstOrDefault(c => c.ProfileId == request.CharacterId)
            ?? throw new InvalidOperationException("This character does not belong to this account.");
        if (entry.WipeOperationId == request.OperationId || entry.Wiped)
        {
            // A repeated confirmation cannot erase a newly recreated character.
            if (entry.Wiped && entry.CreationOperationId.Length == 0)
            {
                RemoveRetiredProfile(entry.ProfileId);
            }

            return GetSnapshot(root, entry.SeasonId, entry.ProfileId);
        }
        if (!entry.Created)
        {
            throw new InvalidOperationException("Finish creating this character before wiping it.");
        }
        EnsureNotInRaid(EffectiveId(root));
        EnsureNotInRaid(entry.ProfileId);
        if (EffectiveId(root) == entry.ProfileId)
        {
            throw new InvalidOperationException("Switch to another character before wiping this one.");
        }

        repository.Runtime(entry.SeasonId);
        var pmc = saves.GetProfile(new MongoId(entry.ProfileId)).CharacterData!.PmcData!;
        var before = SeasonCompiler.Copy(link);
        entry.PreservedAchievements = pmc.Achievements?.ToDictionary(p => p.Key.ToString(), p => p.Value) ?? new();
        entry.Name = pmc.Info?.Nickname ?? "Campaign";
        entry.Wiped = true;
        entry.Created = false;
        entry.WipeOperationId = request.OperationId;
        entry.CreationOperationId = entry.CreationFingerprint = "";
        if (link.SeasonalId == entry.ProfileId)
        {
            link.SeasonalId = null;
            link.CurrentSeasonId = null;
            link.Created = false;
        }
        try
        {
            await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        }
        catch
        {
            _links[root] = before;
            throw;
        }
        // Persist the achievement-only receipt before removing all character progression.
        RemoveRetiredProfile(entry.ProfileId);
        return GetSnapshot(root, entry.SeasonId, entry.ProfileId);
    }

    private async Task ApplySelection(MongoId id, List<string> personal, long revision, string? root = null, string seasonId = "")
    {
        var pmc = saves.GetProfile(id).CharacterData!.PmcData!;
        var state = State(pmc);
        var definition = repository.Runtime(seasonId.Length > 0 ? seasonId : state.SeasonId ?? "").Definition;
        var catalogue = definition.Perks;
        if (state.Revision != revision)
        {
            throw new InvalidOperationException("Perks changed since this screen opened. Refresh and try again.");
        }

        var previousState = State(pmc);
        var previousProgress = pmc.Skills!.Common.Select(skill => (skill, skill.Progress)).ToArray();
        state.RootAccountId ??= root;
        state.SeasonId ??= definition.Id;
        state.GameplayHash ??= SeasonRepository.GameplayHash(definition);
        state.SeasonalPerks = definition.Rules.EnabledCommonIds.Concat(personal).Distinct().ToList();
        ConsumableEffects.UpdateParameters(catalogue, state);
        AllergyEffects.UpdateParameters(
            catalogue,
            state,
            effect => TemplateFilters.Candidates(templates, effect),
            RandomNumberGenerator.GetInt32
        );
        foreach (var p in catalogue.All.Where(p => state.SeasonalPerks.Contains(p.Id)))
        {
            foreach (var e in p.Effects.Where(e => e.EffectId == "skill_level_preset"))
            {
                foreach (var skill in e.SkillIds ?? Enumerable.Empty<string>())
                {
                    var receipt = p.Id + ":" + skill;
                    if (state.AppliedGrants.Contains(receipt))
                    {
                        continue;
                    }

                    if (!Enum.TryParse<SkillTypes>(skill, out var skillId))
                    {
                        continue;
                    }

                    var value = pmc.Skills?.Common?.FirstOrDefault(s => s.Id == skillId);
                    if (value != null)
                    {
                        value.Progress = Math.Max(value.Progress, Math.Clamp(e.IntValue ?? 0, 0, 51) * 100d);
                    }

                    state.AppliedGrants.Add(receipt);
                }
            }
        }
        state.Revision++;
        var effects = new RuntimeEffects(catalogue, state.SeasonalPerks);
        foreach (var skill in pmc.Skills!.Common!)
        {
            skill.Progress = Math.Min(skill.Progress, effects.SkillCap(skill.Id.ToString()) * 100d);
        }
        SetState(pmc, state);
        try
        {
            await saves.SaveProfileAsync(id);
        }
        catch
        {
            foreach (var (skill, progress) in previousProgress)
            {
                skill.Progress = progress;
            }
            SetState(pmc, previousState);
            // Reset SPT's pre-write hash as well as the profile after a failed atomic file write.
            try
            {
                await saves.SaveProfileAsync(id);
            }
            catch
            { /* Original save failure is reported below. */
            }
            throw;
        }
    }

    private static void Validate(Mutation request, SeasonDefinition definition)
    {
        if (request.PerkIds == null)
        {
            throw new InvalidOperationException("A personal perk selection is required.");
        }
        var error = Selection.Validate(definition.Perks, request.PerkIds, definition.Rules, Unsupported(definition.Perks));
        if (error != null)
        {
            throw new InvalidOperationException(error);
        }
    }

    private MongoId CreationCustomization(string side, string parent, string requested)
    {
        if (string.IsNullOrEmpty(requested))
        {
            return DefaultCustomization(side, parent);
        }
        var item = templates.Customization.Values.FirstOrDefault(value => value.Id.ToString() == requested);
        if (item == null || item.Parent != parent || !item.Properties.AvailableAsDefault || !item.Properties.Side.Contains(side))
        {
            throw new InvalidOperationException("The selected head or voice is unavailable for this faction.");
        }
        return item.Id;
    }

    private MongoId DefaultCustomization(string side, string parent)
    {
        return
            templates
                .Customization.Values.Where(item =>
                    item.Parent == parent && item.Properties.AvailableAsDefault && item.Properties.Side.Contains(side)
                )
                .OrderBy(item => item.Id.ToString(), StringComparer.Ordinal)
                .Select(item => item.Id)
                .FirstOrDefault()
                is var id
            && !id.IsEmpty
            ? id
            : throw new InvalidOperationException("SPT has no default head/voice for " + side);
    }

    public void EnsureNotInRaid(string id)
    {
        var root = ResolveRoot(id);
        var location = saves.GetProfile(new MongoId(id)).InraidData?.Location;
        if (Link(root).ActiveRaidProfiles.Count > 0 || (!string.IsNullOrEmpty(location) && location != "none"))
        {
            throw new InvalidOperationException("Finish the raid before changing characters or modifiers.");
        }
    }

    public string ResolveRoot(string sessionId)
    {
        var parent = State(saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!).RootAccountId;
        if (parent == null || parent == sessionId)
        {
            return sessionId;
        }
        if (!saves.ProfileExists(new MongoId(parent)) || !Link(parent).Characters.Any(c => c.ProfileId == sessionId))
        {
            throw new InvalidOperationException("The campaign account link is invalid.");
        }
        return parent;
    }

    public async Task AbortRaid(string root, string character, string raidId, Func<string, Task> cleanup)
    {
        // The account router holds the lease through validation, cleanup and persistence.
        if (!RaidAbortGuard.Matches(Link(root), EffectiveId(root), character, raidId))
        {
            return;
        }
        await cleanup(root);
        var id = new MongoId(character);
        var profile = saves.GetProfile(id);
        if (profile.InraidData != null)
        {
            profile.InraidData.Location = "none";
            await saves.SaveProfileAsync(id);
        }
        var link = Link(root);
        link.ActiveRaidProfiles.Remove(character);
        link.ActiveRaidIds.Remove(character);
        await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
    }

    public async Task MarkRaid(string sessionId, bool active, string? raidId = null)
    {
        var root = ResolveRoot(sessionId);
        using var lease = Enter(root);
        var link = Link(root);
        if (active)
        {
            link.ActiveRaidProfiles.Add(sessionId);
            if (!string.IsNullOrEmpty(raidId))
            {
                link.ActiveRaidIds[sessionId] = raidId;
            }
            else
            {
                link.ActiveRaidIds.Remove(sessionId);
            }
        }
        else
        {
            link.ActiveRaidProfiles.Remove(sessionId);
            link.ActiveRaidIds.Remove(sessionId);
        }
        await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
    }

    private static string NewId()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
    }

    private static string CleanNickname(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Campaign";
        }
        value = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(15).ToArray());
        return value.Length < 3 ? "Campaign" : value;
    }
}
