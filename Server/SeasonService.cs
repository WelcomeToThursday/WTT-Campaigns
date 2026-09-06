using System.Collections.Concurrent;
using System.Security.Cryptography;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Configuration;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Effects.Consumables;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Profiles;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Modding;
using SPTarkov.Server.Core.Services.Profile;

namespace SeasonalPerks.Server;

[Injectable(InjectionType.Singleton)]
public sealed class SeasonService(
    SaveServer saves,
    ProfileDataService profileData,
    CreateProfileService creator,
    TemplateTable templates
)
{
    private const string StateKey = "cjSeasonalPerksState";
    private const string LinkKey = "cjSeasonalPerksAccount";
    private readonly ConcurrentDictionary<string, AccountLink> _links = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    public Catalogue Catalogue { get; } =
        JsonConvert.DeserializeObject<Catalogue>(
            File.ReadAllText(Path.Combine(Metadata.DirectoryPath, "data/catalogue.json"))
        )!;
    public Dictionary<string, string> Locale { get; } =
        JsonConvert.DeserializeObject<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(Metadata.DirectoryPath, "data/locales/en.json"))
        )!;
    public Rules Rules { get; private set; } = new();
    public Dictionary<string, string> Unavailable { get; } = new();

    public void Initialize()
    {
        foreach (var p in Catalogue.All)
        {
            var reason = EffectSupport.UnavailableReason(p);
            if (reason != null)
            {
                Unavailable[p.Id] = reason;
            }
        }
        var path = Path.Combine(Metadata.DirectoryPath, "config.json");
        if (File.Exists(path))
        {
            Rules =
                JsonConvert.DeserializeObject<Rules>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Invalid seasonal rules");
        }
        else
        {
            Rules.EnabledCommonIds = Catalogue
                .Common.Where(p => !Unavailable.ContainsKey(p.Id))
                .Select(p => p.Id)
                .ToList();
            File.WriteAllText(path, JsonConvert.SerializeObject(Rules, Formatting.Indented));
        }
        if (
            Rules.EnabledCommonIds.Any(id =>
                Unavailable.ContainsKey(id) || !Catalogue.Common.Any(p => p.Id == id)
            )
        )
        {
            throw new InvalidDataException(
                "EnabledCommonIds contains an unsupported or unknown global modifier."
            );
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

    private AccountLink Link(string root) =>
        _links.GetOrAdd(
            root,
            id =>
                profileData
                    .GetProfileDataAsync<AccountLink>(new MongoId(id), LinkKey)
                    .GetAwaiter()
                    .GetResult()
                ?? new AccountLink()
        );

    public string EffectiveId(string root)
    {
        var link = Link(root);
        return link.Mode == "seasonal" && link.Created ? link.SeasonalId! : root;
    }

    public bool IsSeasonal(string id) =>
        State(saves.GetProfile(new MongoId(id)).CharacterData!.PmcData!).Revision > 0;

    public static PerkState State(PmcData pmc)
    {
        if (pmc.ExtensionData?.TryGetValue(StateKey, out var raw) != true)
        {
            return new PerkState();
        }

        var json = raw is System.Text.Json.JsonElement j ? j.GetString() : raw?.ToString();
        return string.IsNullOrWhiteSpace(json)
            ? new PerkState()
            : JsonConvert.DeserializeObject<PerkState>(json)!;
    }

    private static void SetState(PmcData pmc, PerkState state)
    {
        pmc.ExtensionData ??= new();
        // Keep the persisted state contract independent of SPT's System.Text.Json serializer.
        // State and creation receipts are committed in the SAME profile save as the grant.
        pmc.ExtensionData[StateKey] = JsonConvert.SerializeObject(state);
    }

    public ServerSnapshot GetSnapshot(string root)
    {
        var link = Link(root);
        var normal = saves.GetProfile(new MongoId(root)).CharacterData!.PmcData!;
        var seasonal = link.Created
            ? saves.GetProfile(new MongoId(link.SeasonalId!)).CharacterData!.PmcData
            : null;
        return new ServerSnapshot
        {
            Catalogue = Catalogue,
            Locale = Locale,
            Unavailable = Unavailable,
            Rules = Rules,
            ActiveMode = link.Mode,
            EffectiveProfileId = EffectiveId(root),
            State = seasonal == null ? new PerkState() : State(seasonal),
            Characters = new()
            {
                new()
                {
                    Mode = "normal",
                    Name = normal.Info?.Nickname ?? "PMC",
                    Level = normal.Info?.Level ?? 1,
                    Exists = true,
                    Side = normal.Info?.Side ?? "Usec",
                    Visual = Visual(normal),
                },
                new()
                {
                    Mode = "seasonal",
                    Name = seasonal?.Info?.Nickname ?? "Create seasonal character",
                    Level = seasonal?.Info?.Level ?? 1,
                    Exists = seasonal != null,
                    Side = seasonal?.Info?.Side ?? "Usec",
                    Visual = seasonal == null ? null : Visual(seasonal),
                },
            },
        };
    }

    private CharacterVisual? Visual(PmcData profile)
    {
        if (
            profile.Inventory?.Items == null
            || profile.Inventory.Equipment == null
            || profile.Info == null
        )
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
                if (item.ParentId != null && ids.Contains(item.ParentId.ToString()))
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
        return new RuntimeEffects(Catalogue, State(pmc).SeasonalPerks);
    }

    public async Task<ServerSnapshot> Create(string root, Mutation request)
    {
        var link = Link(root);
        if (link.Created)
        {
            throw new InvalidOperationException("A seasonal character already exists.");
        }

        if (request.Side is not ("Usec" or "Bear"))
        {
            throw new InvalidOperationException("Invalid faction.");
        }

        Validate(request);
        var headId = CreationCustomization(
            request.Side,
            "5cc085e214c02e000c6bea67",
            request.HeadId
        );
        var voiceId = CreationCustomization(
            request.Side,
            "5fc100cf95572123ae738483",
            request.VoiceId
        );
        var account = saves.GetProfile(new MongoId(root));
        EnsureNotInRaid(root);
        if (link.SeasonalId == null)
        {
            link.SeasonalId = NewId();
        }
        await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        var id = new MongoId(link.SeasonalId);
        if (!saves.ProfileExists(id))
        {
            saves.CreateProfile(
                new Info
                {
                    ProfileId = id,
                    ScavengerId = new MongoId(NewId()),
                    Aid = saves.GetProfiles().Values.Max(p => p.ProfileInfo?.Aid ?? 0) + 1,
                    Username = (account.ProfileInfo!.Username ?? "PMC") + "-seasonal",
                    Edition = account.ProfileInfo.Edition,
                    IsWiped = false,
                }
            );
        }

        var existing = saves.GetProfile(id).CharacterData!.PmcData!;
        if (existing.Info == null)
        {
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
        // Resume a creation interrupted after its profile/receipts were committed.
        if (State(saves.GetProfile(id).CharacterData!.PmcData!).Revision == 0)
        {
            await ApplySelection(id, request.PerkIds, 0, root);
        }

        link.Created = true;
        try
        {
            await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        }
        catch
        {
            link.Created = false;
            throw;
        }
        return GetSnapshot(root);
    }

    public async Task<ServerSnapshot> Edit(string root, Mutation request)
    {
        var link = Link(root);
        if (!link.Created)
        {
            throw new InvalidOperationException("Create a seasonal character first.");
        }

        if (!Rules.AllowEdits)
        {
            throw new InvalidOperationException("Perk editing is disabled in the server settings.");
        }

        EnsureNotInRaid(EffectiveId(root));
        Validate(request);
        await ApplySelection(
            new MongoId(link.SeasonalId!),
            request.PerkIds,
            request.ExpectedRevision
        );
        return GetSnapshot(root);
    }

    public async Task<ServerSnapshot> Switch(string root, string mode)
    {
        var link = Link(root);
        if (mode is not ("normal" or "seasonal"))
        {
            throw new InvalidOperationException("Unknown character mode.");
        }

        if (mode == "seasonal" && !link.Created)
        {
            throw new InvalidOperationException("Create a seasonal character first.");
        }

        var current = EffectiveId(root);
        EnsureNotInRaid(current);
        await saves.SaveProfileAsync(new MongoId(current));
        var previous = link.Mode;
        link.Mode = mode;
        try
        {
            await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
        }
        catch
        {
            link.Mode = previous;
            throw;
        }
        return GetSnapshot(root);
    }

    private async Task ApplySelection(
        MongoId id,
        List<string> personal,
        long revision,
        string? root = null
    )
    {
        var pmc = saves.GetProfile(id).CharacterData!.PmcData!;
        var state = State(pmc);
        if (state.Revision != revision)
        {
            throw new InvalidOperationException(
                "Perks changed since this screen opened. Refresh and try again."
            );
        }

        var previousState = State(pmc);
        var previousProgress = pmc.Skills!.Common!.Select(skill => (skill, skill.Progress))
            .ToArray();
        state.RootAccountId ??= root;
        state.SeasonalPerks = Rules.EnabledCommonIds.Concat(personal).Distinct().ToList();
        ConsumableEffects.UpdateParameters(Catalogue, state);
        AllergyEffects.UpdateParameters(
            Catalogue,
            state,
            effect => SeasonalPerks.Server.Effects.TemplateFilters.Candidates(templates, effect),
            RandomNumberGenerator.GetInt32
        );
        foreach (var p in Catalogue.All.Where(p => state.SeasonalPerks.Contains(p.Id)))
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
                        value.Progress = Math.Max(
                            value.Progress,
                            Math.Clamp(e.IntValue ?? 0, 0, 51) * 100d
                        );
                    }

                    state.AppliedGrants.Add(receipt);
                }
            }
        }
        state.Revision++;
        var effects = new RuntimeEffects(Catalogue, state.SeasonalPerks);
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

    private void Validate(Mutation request)
    {
        if (request.PerkIds == null)
        {
            throw new InvalidOperationException("A personal perk selection is required.");
        }
        var error = Selection.Validate(Catalogue, request.PerkIds, Rules, Unavailable);
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
        var item = templates.Customization.Values.FirstOrDefault(value =>
            value.Id.ToString() == requested
        );
        if (
            item == null
            || item.Parent != parent
            || !item.Properties.AvailableAsDefault
            || !item.Properties.Side.Contains(side)
        )
        {
            throw new InvalidOperationException(
                "The selected head or voice is unavailable for this faction."
            );
        }
        return item.Id;
    }

    private MongoId DefaultCustomization(string side, string parent)
    {
        return
            templates
                .Customization.Values.Where(item =>
                    item.Parent == parent
                    && item.Properties.AvailableAsDefault
                    && item.Properties.Side.Contains(side)
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
        if (
            Link(root).ActiveRaidProfiles.Count > 0
            || (!string.IsNullOrEmpty(location) && location != "none")
        )
        {
            throw new InvalidOperationException(
                "Finish the raid before changing characters or modifiers."
            );
        }
    }

    public string ResolveRoot(string sessionId)
    {
        var parent = State(
            saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!
        ).RootAccountId;
        if (parent == null || parent == sessionId)
        {
            return sessionId;
        }
        if (!saves.ProfileExists(new MongoId(parent)) || Link(parent).SeasonalId != sessionId)
        {
            throw new InvalidOperationException("The seasonal account link is invalid.");
        }
        return parent;
    }

    public async Task MarkRaid(string sessionId, bool active)
    {
        var root = ResolveRoot(sessionId);
        using var lease = Enter(root);
        var link = Link(root);
        if (active)
        {
            link.ActiveRaidProfiles.Add(sessionId);
        }
        else
        {
            link.ActiveRaidProfiles.Remove(sessionId);
        }
        await profileData.SaveProfileDataAsync(new MongoId(root), LinkKey, link);
    }

    private static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));

    private static string CleanNickname(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Seasonal";
        }
        value = new string(
            value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(15).ToArray()
        );
        return value.Length < 3 ? "Seasonal" : value;
    }
}
