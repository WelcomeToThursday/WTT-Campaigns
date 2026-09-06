using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Seasons;

namespace SeasonalPerks.Server.Web.Pages;

public partial class Creator
{
    private static readonly string[] Sections =
    [
        "Overview",
        "Starting character",
        "Perks",
        "Documents",
        "Battle pass",
        "Rewards",
        "Items and crates",
        "Quests",
        "Localization",
        "Preview and publish",
    ];

    private static string SectionDescription(string section)
    {
        return section switch
        {
            "Overview" => "Identity, branding and the introduction players see.",
            "Starting character" => "Configure each faction’s starting equipment, inventory and skills.",
            "Perks" => "Build modifiers, balance point costs and define conflicts.",
            "Documents" => "Set document types, collection limits and map-specific placement caps.",
            "Battle pass" => "Arrange reward pages and set the requirements to unlock them.",
            "Rewards" => "Arrange the seasonal reward grid and edit selected tiles in the inspector.",
            "Items and crates" => "Reuse installed models and configure exchanges and weighted loot pools.",
            "Quests" => "Create objectives, prerequisite chains and completion rewards.",
            "Localization" => "Edit English text and translations with English fallback.",
            _ => "Simulate progress, review validation and prepare a shareable season pack.",
        };
    }

    private static readonly string[] EquipmentSlots =
    [
        "Headwear",
        "FaceCover",
        "Eyewear",
        "Earpiece",
        "ArmorVest",
        "TacticalVest",
        "Backpack",
        "FirstPrimaryWeapon",
        "SecondPrimaryWeapon",
        "Holster",
        "Scabbard",
        "SecuredContainer",
    ];
    private static readonly string[] MapNames =
    [
        "bigmap",
        "factory4_day",
        "factory4_night",
        "interchange",
        "laboratory",
        "lighthouse",
        "rezervbase",
        "sandbox",
        "sandbox_high",
        "shoreline",
        "tarkovstreets",
        "woods",
    ];
    private DraftEnvelope? _draft;
    private string _baseline = "";
    private string _section = "Overview";
    private string _message = "";
    private string? _published;
    private Action? _discard;
    private string _faction = "Usec",
        _map = "bigmap",
        _language = "en",
        _newLanguage = "",
        _localeSearch = "";
    private int _pageIndex;
    private string _questRewardKind = "Item";
    private SeasonReward? _reward,
        _dragged;
    private SeasonValidationResult? _validation;
    private readonly Dictionary<PerkEffect, JObject> _effectObjects = new();
    private int _previewLevel = 1,
        _previewClassified;
    private string _previewFaction = "USEC";
    private readonly Dictionary<string, int> _previewDocuments = new();
    private readonly HashSet<string> _previewQuests = new(),
        _previewClaims = new(),
        _previewPerks = new();
    private SeasonDefinition S
    {
        get { return _draft!.Definition; }
    }

    private bool DirtyState
    {
        get { return _draft != null && _baseline != JsonConvert.SerializeObject(S); }
    }

    private IEnumerable<string> ArtAssets
    {
        get { return SeasonCompiler.Assets(S); }
    }

    private string DefaultImage
    {
        get { return Repository.Legacy.UniversalImage; }
    }

    private FactionStartingSetup Starter
    {
        get { return _faction == "Bear" ? S.Starting.Bear : S.Starting.Usec; }
    }

    private IEnumerable<ContentChoice> OwnedItems
    {
        get
        {
            return S
                .Items.Select(i => new ContentChoice(i.Id, i.Name))
                .Concat(S.ImportedItems.Properties().Select(p => new ContentChoice(p.Name, (string?)p.Value["_name"] ?? p.Name)));
        }
    }

    private IEnumerable<ContentChoice> OwnedQuests
    {
        get { return S.Quests.OfType<JObject>().Select(q => new ContentChoice((string)q["_id"]!, QuestName(q))); }
    }

    private int PageIndex
    {
        get { return Math.Clamp(_pageIndex, 0, Math.Max(0, S.Pages.Count - 1)); }
        set
        {
            _pageIndex = value;
            _reward = null;
        }
    }
    private int Columns
    {
        get { return _section == "Rewards" ? 5 : 2; }
    }

    private int Rows
    {
        get { return _section == "Rewards" ? 2 : 3; }
    }

    private List<SeasonReward> Tiles
    {
        get
        {
            return _section == "Rewards" ? S.SeasonalRewards
                : S.Pages.Count > 0 ? S.Pages[PageIndex].Rewards
                : [];
        }
    }

    private IEnumerable<(string Key, string Label, PerkEffect Effect)> EffectTemplates
    {
        get
        {
            return Repository
                .Legacy.Perks.All.SelectMany(p =>
                    p.Effects.Select(
                        (e, i) =>
                            (
                                Key: p.Id + ":" + i,
                                Label: (e.EffectId ?? "Effect")
                                    + " · "
                                    + Repository.Legacy.Locales["en"].GetValueOrDefault(p.Id + " name", p.Id),
                                Effect: e
                            )
                    )
                )
                .Where(t => EffectSupport.UnavailableReason(new Perk { Effects = [t.Effect] }) == null);
        }
    }

    private static string Text(ChangeEventArgs e)
    {
        return e.Value?.ToString() ?? "";
    }

    private static int Int(ChangeEventArgs e, int fallback)
    {
        return int.TryParse(Text(e), out var n) ? n : fallback;
    }

    private static double Number(ChangeEventArgs e, double fallback)
    {
        return double.TryParse(Text(e), out var n) && double.IsFinite(n) ? n : fallback;
    }

    private static string AssetUrl(string id)
    {
        return "/wtt-seasonal/creator/assets/" + id + ".png";
    }

    private void Dirty()
    {
        _validation = null;
    }

    private void Open(DraftEnvelope draft)
    {
        _draft = draft;
        _baseline = JsonConvert.SerializeObject(S);
        _section = "Overview";
        _pageIndex = 0;
        _validation = null;
        _reward = null;
        _published = null;
        _language = "en";
        _effectObjects.Clear();
        _message = "";
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            _message = e.Message;
        }
    }

    private void Leave(Action next)
    {
        if (DirtyState)
        {
            _discard = next;
        }
        else
        {
            next();
        }
    }

    private void ConfirmDiscard()
    {
        var next = _discard;
        _discard = null;
        next?.Invoke();
    }

    private void BeforeNavigate(LocationChangingContext context)
    {
        if (DirtyState)
        {
            context.PreventNavigation();
            _message = "Save your draft before navigating away.";
        }
    }

    private void Save()
    {
        Run(() =>
        {
            _draft = Repository.Save(_draft!);
            _baseline = JsonConvert.SerializeObject(S);
            _effectObjects.Clear();
            _reward = _reward == null ? null : S.AllRewards.FirstOrDefault(r => r.Id == _reward.Id);
            _message = "Draft saved.";
        });
    }

    private void Validate()
    {
        Run(() =>
        {
            _validation = Content.Validate(S);
            _section = "Preview and publish";
            _message = "Validation complete.";
        });
    }

    private void Publish()
    {
        Run(() =>
        {
            _validation = Content.Validate(S);
            _section = "Preview and publish";
            if (DirtyState)
            {
                throw new InvalidOperationException("Save your changes before publishing.");
            }

            _published = Repository.Publish(_draft!, _validation);
            _message = "Pack published. Export it or select it for activation.";
        });
    }

    private void Queue(string key)
    {
        var definition = Repository.Pack(key);
        var validation = Content.Validate(definition);
        if (key != "legacy" && !validation.CanActivate)
        {
            throw new InvalidOperationException(
                string.Join("; ", validation.Issues.Where(i => i.Severity != "warning").Select(i => i.Message).Take(5))
            );
        }

        Repository.Queue(key);
        _message = "Activation scheduled for the next server restart.";
    }

    private void EditPublished(string key)
    {
        Run(() => Open(Repository.Save(new DraftEnvelope { Id = SeasonRepository.NewId(), Definition = Repository.Pack(key) })));
    }

    private async Task Import(InputFileChangeEventArgs args)
    {
        try
        {
            await using var input = args.File.OpenReadStream(64 * 1024 * 1024);
            using var bytes = new MemoryStream();
            await input.CopyToAsync(bytes);
            Open(Repository.Import(bytes.ToArray()));
            _message = "Pack imported as a draft. Validate it against this server before publishing.";
        }
        catch (Exception e)
        {
            _message = e.Message;
        }
    }

    private string PerkName(Perk perk)
    {
        return S.Locales["en"].GetValueOrDefault(perk.Id + " name", "New perk");
    }

    private void AddPerk(bool common)
    {
        var perk = new Perk
        {
            Id = SeasonRepository.NewId(),
            Type = common ? "common" : "personal",
            ImageUrl = DefaultImage,
            Points = common ? null : -1,
        };
        (common ? S.Perks.Common : S.Perks.Personal).Add(perk);
        S.Locales["en"][perk.Id + " name"] = "New perk";
        S.Locales["en"][perk.Id + " description"] = "";
    }

    private void RemovePerk(Perk perk)
    {
        S.Perks.Common.Remove(perk);
        S.Perks.Personal.Remove(perk);
        S.Rules.EnabledCommonIds.Remove(perk.Id);
        foreach (var other in S.Perks.All)
        {
            other.Conflicts.Remove(perk.Id);
        }

        foreach (var locale in S.Locales.Values)
        {
            locale.Remove(perk.Id + " name");
            locale.Remove(perk.Id + " description");
        }
    }

    private void Common(string id, bool enabled)
    {
        S.Rules.EnabledCommonIds.Remove(id);
        if (enabled)
        {
            S.Rules.EnabledCommonIds.Add(id);
        }
    }

    private static void Conflict(Perk perk, Perk other, bool enabled)
    {
        perk.Conflicts.Remove(other.Id);
        other.Conflicts.Remove(perk.Id);
        if (enabled)
        {
            perk.Conflicts.Add(other.Id);
            other.Conflicts.Add(perk.Id);
        }
    }

    private void AddEffect(Perk perk, string key)
    {
        var found = EffectTemplates.FirstOrDefault(t => t.Key == key);
        if (found.Effect != null)
        {
            perk.Effects.Add(SeasonCompiler.Copy(found.Effect));
        }
    }

    private JObject EffectObject(PerkEffect effect)
    {
        if (!_effectObjects.TryGetValue(effect, out var value))
        {
            _effectObjects[effect] = value = JObject.FromObject(effect);
        }
        return value;
    }

    private void UpdateEffect(Perk perk, int index, PerkEffect previous)
    {
        var value = _effectObjects[previous];
        var next = value.ToObject<PerkEffect>()!;
        perk.Effects[index] = next;
        _effectObjects.Remove(previous);
        _effectObjects[next] = value;
        Dirty();
    }

    private void AddDocument()
    {
        if (S.Documents.Count < 8)
        {
            S.Documents.Add(
                new()
                {
                    Id = SeasonRepository.NewId(),
                    Image = DefaultImage,
                    UnavailableImage = DefaultImage,
                }
            );
        }
    }

    private void CloneDocument(SeasonDocument document)
    {
        if (string.IsNullOrEmpty(document.ItemId))
        {
            _message = "Choose a source item first.";
            return;
        }
        var id = SeasonRepository.NewId();
        S.Items.Add(
            new()
            {
                Id = id,
                CloneFrom = document.ItemId,
                Name = document.Name,
            }
        );
        document.ItemId = id;
        _message = "Item created. Edit its dimensions and stack limit under Items and crates.";
    }

    private void AddPage()
    {
        S.Pages.Add(new());
        PageIndex = S.Pages.Count - 1;
    }

    private void DeletePage()
    {
        if (S.Pages.Count <= 1)
        {
            _message = "Keep at least one battle pass page.";
            return;
        }
        S.Pages.RemoveAt(PageIndex);
        _reward = null;
    }

    private void MovePage(int delta)
    {
        var target = PageIndex + delta;
        if (target < 0 || target >= S.Pages.Count)
        {
            return;
        }
        var page = S.Pages[PageIndex];
        S.Pages.RemoveAt(PageIndex);
        S.Pages.Insert(target, page);
        PageIndex = target;
    }

    private static SeasonReward FreshReward(SeasonReward original)
    {
        var copy = SeasonCompiler.Copy(original);
        copy.Id = SeasonRepository.NewId();
        copy.Name += " copy";
        foreach (var grant in copy.Grants.OfType<JObject>())
        {
            grant["id"] = SeasonRepository.NewId();
        }

        return copy;
    }

    private void DuplicatePage()
    {
        if (S.Pages.Count == 0)
        {
            return;
        }
        var page = S.Pages[PageIndex];
        S.Pages.Insert(
            PageIndex + 1,
            new() { Rewards = page.Rewards.Select(FreshReward).ToList(), PreviousRequirement = page.PreviousRequirement }
        );
        PageIndex++;
    }

    private (int X, int Y)? EmptyCell()
    {
        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Columns; x++)
            {
                if (Tiles.All(t => x < t.X || x >= t.X + t.Width || y < t.Y || y >= t.Y + t.Height))
                {
                    return (x, y);
                }
            }
        }

        return null;
    }

    private void AddReward()
    {
        var cell = EmptyCell();
        if (cell == null)
        {
            _message = "This grid is full. Add a page or remove a tile.";
            return;
        }
        _reward = new()
        {
            Id = SeasonRepository.NewId(),
            X = cell.Value.X,
            Y = cell.Value.Y,
            Image = DefaultImage,
            BigImage = DefaultImage,
        };
        Tiles.Add(_reward);
    }

    private void DuplicateReward()
    {
        var source = _reward;
        var cell = EmptyCell();
        if (source == null || cell == null)
        {
            return;
        }
        _reward = FreshReward(source);
        _reward.X = cell.Value.X;
        _reward.Y = cell.Value.Y;
        _reward.Width = _reward.Height = 1;
        Tiles.Add(_reward);
    }

    private void DeleteReward()
    {
        if (_reward != null)
        {
            Tiles.Remove(_reward);
        }
        _reward = null;
    }

    private void Drop(int x, int y)
    {
        if (_dragged == null)
        {
            return;
        }

        if (
            x + _dragged.Width > Columns
            || y + _dragged.Height > Rows
            || Tiles.Any(t =>
                t != _dragged && x < t.X + t.Width && x + _dragged.Width > t.X && y < t.Y + t.Height && y + _dragged.Height > t.Y
            )
        )
        {
            _message = "That position overlaps another tile or leaves the grid.";
            return;
        }
        _dragged.X = x;
        _dragged.Y = y;
        _reward = _dragged;
        _dragged = null;
        Dirty();
    }

    private static void ChangePool(SeasonCrate crate, string previous, string next)
    {
        if (next.Length == 0 || crate.Pool.ContainsKey(next))
        {
            return;
        }
        var weight = crate.Pool[previous];
        crate.Pool.Remove(previous);
        crate.Pool[next] = weight;
    }

    private IEnumerable<KeyValuePair<string, string>> LocaleEntries
    {
        get
        {
            return SeasonCompiler
                .Texts(S)
                .Select(p => new KeyValuePair<string, string>(
                    p.Key,
                    _language == "en" ? p.Value : S.Locales[_language].GetValueOrDefault(p.Key, "")
                ));
        }
    }

    private void SetLocale(string key, string value)
    {
        S.Locales[_language][key] = value;
        if (_language != "en")
        {
            return;
        }

        if (key == S.Id + " name")
        {
            S.Name = value;
        }

        if (key == S.Id + " description")
        {
            S.Description = value;
        }

        foreach (var item in S.Items)
        {
            if (key == item.Id + " Name" || key == item.Id + " ShortName")
            {
                item.Name = value;
            }

            if (key == item.Id + " Description")
            {
                item.Description = value;
            }
        }
        foreach (var doc in S.Documents)
        {
            if (key == doc.Id + " name")
            {
                doc.Name = value;
            }
        }

        foreach (var reward in S.AllRewards)
        {
            if (key == reward.Id + " name")
            {
                reward.Name = value;
            }

            if (key == reward.Id + " description")
            {
                reward.Description = value;
            }
        }
        for (var i = 0; i < S.Slides.Count; i++)
        {
            if (key == S.Id + " slide " + i + " text")
            {
                S.Slides[i].Text = value;
            }
        }
        foreach (var quest in S.Quests.OfType<JObject>())
        {
            if (quest["localization"]?["en"] is JObject locale && locale.ContainsKey(key))
            {
                locale[key] = value;
            }
        }
    }

    private void AddLanguage()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(_newLanguage, "^[a-z]{2}(-[A-Z]{2})?$"))
        {
            _message = "Use a language code such as en, fr, or pt-BR.";
            return;
        }
        S.Locales.TryAdd(_newLanguage, new());
        _language = _newLanguage;
        _newLanguage = "";
    }

    private static void Toggle(HashSet<string> set, string id, bool enabled)
    {
        if (enabled)
        {
            set.Add(id);
        }
        else
        {
            set.Remove(id);
        }
    }

    private string PreviewReason(SeasonReward reward)
    {
        if (!reward.Enabled)
        {
            return "Disabled";
        }

        if (_previewClaims.Contains(reward.Id))
        {
            return "Claimed";
        }

        if (reward.Side.Length > 0 && !string.Equals(reward.Side, _previewFaction, StringComparison.OrdinalIgnoreCase))
        {
            return "Other faction";
        }

        var page = S.Pages.FindIndex(p => p.Rewards.Contains(reward));
        if (page > 0 && S.Pages[page - 1].Rewards.Count(r => _previewClaims.Contains(r.Id)) < S.Pages[page].PreviousRequirement)
        {
            return "Previous page locked";
        }

        foreach (var c in reward.Conditions)
        {
            if ((string?)c["conditionType"] == "Level" && _previewLevel < (int)c["value"]!)
            {
                return "Requires level " + c["value"];
            }

            if ((string?)c["conditionType"] == "Quest" && !_previewQuests.Contains((string)c["target"]!))
            {
                return "Quest incomplete";
            }
        }
        if (reward.Costs.Sum(c => Math.Max(0, c.Count - _previewDocuments.GetValueOrDefault(c.DocumentId))) > _previewClassified)
        {
            return "Insufficient documents";
        }

        return "Requirements met (validate installed dependencies before publishing)";
    }

    private void GoToIssue(string path)
    {
        var section = path.Split('/')[0];
        _section = section switch
        {
            "Assets" => "Overview",
            "Items" or "Exchanges" => "Items and crates",
            _ => Sections.Contains(section) ? section : "Overview",
        };
        if (section == "Rewards")
        {
            _reward = S.AllRewards.FirstOrDefault(r => path.EndsWith(r.Id, StringComparison.Ordinal));
            var page = S.Pages.FindIndex(p => p.Rewards.Contains(_reward!));
            if (page >= 0)
            {
                _section = "Battle pass";
                _pageIndex = page;
            }
        }
    }

    private void AddQuest()
    {
        var id = SeasonRepository.NewId();
        var quest = new JObject
        {
            ["_id"] = id,
            ["QuestName"] = "New quest",
            ["name"] = id + " name",
            ["description"] = id + " description",
            ["traderId"] = "54cb50c76803fa8b248b4571",
            ["location"] = "any",
            ["image"] = "/files/quest/icon/596b36c586f77450d6045ad2.jpg",
            ["type"] = "PickUp",
            ["side"] = "Pmc",
            ["canShowNotificationsInGame"] = true,
            ["restartable"] = false,
            ["instantComplete"] = false,
            ["conditions"] = new JObject
            {
                ["AvailableForStart"] = new JArray(),
                ["AvailableForFinish"] = new JArray(),
                ["Fail"] = new JArray(),
            },
            ["rewards"] = new JObject
            {
                ["Started"] = new JArray(),
                ["Success"] = new JArray(),
                ["Fail"] = new JArray(),
            },
            ["localization"] = new JObject
            {
                ["en"] = new JObject { [id + " name"] = "New quest", [id + " description"] = "Complete the objectives." },
            },
        };
        foreach (
            var key in new[]
            {
                "startedMessageText",
                "successMessageText",
                "failMessageText",
                "acceptPlayerMessage",
                "completePlayerMessage",
                "declinePlayerMessage",
            }
        )
        {
            quest[key] = id + " " + key;
            quest["localization"]!["en"]![id + " " + key] = key == "successMessageText" ? "Well done." : "I have a task for you.";
        }
        AddCondition(quest, "AvailableForStart", "Level");
        AddCondition(quest, "AvailableForFinish", "HandoverItem");
        S.Quests.Add(quest);
    }

    private static string QuestLocale(JObject quest, string field)
    {
        return (string?)quest["localization"]?["en"]?[(string?)quest[field] ?? ""] ?? "";
    }

    private static string QuestName(JObject quest)
    {
        return QuestLocale(quest, "name") is { Length: > 0 } name ? name : (string?)quest["QuestName"] ?? (string)quest["_id"]!;
    }

    private static void QuestText(JObject quest, string field, string value)
    {
        var key = (string?)quest[field] ?? (string)quest["_id"]! + " " + field;
        quest[field] = key;
        quest["localization"] ??= new JObject();
        quest["localization"]!["en"] ??= new JObject();
        quest["localization"]!["en"]![key] = value;
        if (field == "name")
        {
            quest["QuestName"] = value;
        }
    }

    private static void AddCondition(JObject quest, string stage, string kind)
    {
        if (kind.Length == 0)
        {
            return;
        }

        var id = SeasonRepository.NewId();
        var condition = new JObject
        {
            ["id"] = id,
            ["conditionType"] = kind,
            ["index"] = ((JArray)quest["conditions"]![stage]!).Count,
            ["parentId"] = "",
            ["dynamicLocale"] = false,
            ["visibilityConditions"] = new JArray(),
            ["value"] = 1,
            ["compareMethod"] = ">=",
        };
        if (kind is "FindItem" or "HandoverItem")
        {
            condition["target"] = new JArray("");
            condition["onlyFoundInRaid"] = false;
            condition["minDurability"] = 0;
            condition["maxDurability"] = 100;
        }
        if (kind == "Quest")
        {
            condition["target"] = "";
            condition["status"] = new JArray(4);
            condition["availableAfter"] = 0;
        }
        if (kind == "TraderLoyalty")
        {
            condition["target"] = "54cb50c76803fa8b248b4571";
        }

        ((JArray)quest["conditions"]![stage]!).Add(condition);
        quest["localization"]!["en"]![id] = kind == "HandoverItem" ? "Hand over the required items" : "Complete the requirement";
    }

    private void AddQuestReward(JObject quest)
    {
        if (_questRewardKind != "Item")
        {
            ((JArray)quest["rewards"]!["Success"]!).Add(
                new JObject
                {
                    ["id"] = SeasonRepository.NewId(),
                    ["type"] = _questRewardKind,
                    ["target"] = "",
                    ["value"] = _questRewardKind == "Experience" ? 100 : 1,
                }
            );
            return;
        }
        var id = SeasonRepository.NewId();
        ((JArray)quest["rewards"]!["Success"]!).Add(
            new JObject
            {
                ["id"] = SeasonRepository.NewId(),
                ["type"] = "Item",
                ["target"] = id,
                ["value"] = 1,
                ["items"] = new JArray(
                    new JObject
                    {
                        ["_id"] = id,
                        ["_tpl"] = "",
                        ["upd"] = new JObject { ["StackObjectsCount"] = 1 },
                    }
                ),
            }
        );
    }
}
