namespace WTT.Campaigns.Server.Web.Pages;

public partial class Creator
{
    private readonly Stack<(string Section, string Id, string Child)> _workspaceHistory = new();

    private void NavigateWorkspace(string path)
    {
        _workspaceHistory.Push((_section, _focusId, _focusChildId));
        _focusId = "";
        _focusChildId = "";
        GoToIssue(path);
    }

    private void BackWorkspace()
    {
        if (_workspaceHistory.TryPop(out var previous))
        {
            _section = previous.Section;
            _focusId = previous.Id;
            _focusChildId = previous.Child;
        }
    }

    [Microsoft.AspNetCore.Components.SupplyParameterFromQuery(Name = "draft")]
    public string? DraftQuery { get; set; }

    [Microsoft.AspNetCore.Components.SupplyParameterFromQuery(Name = "section")]
    public string? SectionQuery { get; set; }

    [Microsoft.AspNetCore.Components.SupplyParameterFromQuery(Name = "content")]
    public string? ContentQuery { get; set; }

    private string? _openedQuery;
    private string _focusChildId = "";

    protected override void OnParametersSet()
    {
        ApplyGuidanceQuery();
        if (DraftQuery == null || _openedQuery == DraftQuery)
        {
            return;
        }

        _openedQuery = DraftQuery;
        Run(() =>
        {
            Open(Repository.Load(DraftQuery));
            if (SectionQuery != null && Sections.Contains(SectionQuery))
            {
                _section = SectionQuery;
            }

            if (!string.IsNullOrEmpty(ContentQuery))
            {
                GoToIssue((_section == "Quests" ? "Quests/" : "Story/") + ContentQuery);
            }
        });
    }

    private readonly Dictionary<string, string> _contentSelection = new();

    private string SelectedContent(string key, IEnumerable<WTT.Campaigns.Server.Seasons.ContentChoice> choices)
    {
        var records = choices.ToArray();
        var selected = _contentSelection.GetValueOrDefault(key, "");
        if (!records.Any(c => c.Id == selected))
        {
            _contentSelection[key] = selected = records.FirstOrDefault()?.Id ?? "";
        }

        return selected;
    }

    private void RemoveContent(object content, Action remove)
    {
        var uses = WTT.Campaigns.Server.Web.Authoring.StoryAuthoring.Uses(S, content);
        if (uses.Count > 0)
        {
            _message = "Remove or reassign these references first: " + string.Join(", ", uses);
            return;
        }

        remove();
        Dirty();
    }

    private static readonly (string Name, string[] Sections)[] NavigationGroups =
    [
        ("Season setup", ["Overview", "Starting character", "Perks"]),
        ("Rewards and economy", ["Documents", "Battle pass", "Rewards", "Items and crates"]),
        (
            "Quests and story",
            [
                "Chapters",
                "Quests",
                "Journal notes",
                "Conversations",
                "Variables",
                "Entry points",
                "Raid events",
                "Zones and captures",
                "Story media",
            ]
        ),
        ("Review and publish", ["Localization", "Story rehearsal", "Preview and publish"]),
    ];

    private static string SectionTitle(string section)
    {
        return WTT.Campaigns.Server.Web.Authoring.CreatorGuidance.SectionTitle(section);
    }

    private static string SectionGuidance(string section)
    {
        return section switch
        {
            "Overview" =>
                "Set the season's identity and introduction. Use a new season identity for gameplay changes after characters have used it.",
            "Starting character" =>
                "Choose a starter edition, then add stash contents or replace equipment. USEC and BEAR have independent setups. Skills range from 0 to 51.",
            "Perks" =>
                "Benefits normally cost negative points; drawbacks add positive points. Check conflicts and test the resulting budget before publishing.",
            "Documents" =>
                "The raid limit is eight ordinary documents. Map overrides replace the default; collection allowance can reduce it. The default window is 23 hours.",
            "Battle pass" or "Rewards" =>
                "Select a tile to edit its contents in the inspector. Drag it to move, or edit its dimensions. Page gates count enabled tiles on the previous page.",
            "Items and crates" =>
                "Clone an installed item model, then define dimensions and stack size. Crate pool weights are relative chances, not percentages.",
            "Quests" => "Create non-story quests here. Story quests are created and edited inside their chapter in Chapters and quests.",
            "Chapters" =>
                "Choose a chapter, then create or select one of its quests here. Chapter settings, objectives, rewards and story behavior stay in this workspace.",
            "Journal notes" =>
                "Notes appear when an action publishes them or a linked quest reaches a configured status. Creating a note alone does not reveal it.",
            "Conversations" =>
                "Start with a template, choose a trader, and edit its text. NPC lines must advance their phase. Two eligible automatic NPC lines are ambiguous and rejected.",
            "Variables" =>
                "Use Dialogue scope for a conversation's temporary phase. Use Profile scope for durable story decisions and Session scope for reconnect-reset state.",
            "Entry points" =>
                "A dialogue needs an entry point to become available. Its trader must match the conversation. A named start point initializes the main variable.",
            "Raid events" =>
                "Use verified scene targets. Collectibles reference generated loot; bindings do not place loot. Survival-dependent actions wait until the raid ends successfully.",
            "Story media" =>
                "Register the exact finalized bundle hash and asset name. Install bundles under the client's StoryMedia directory separately; Creator ZIPs contain definitions and PNGs.",
            "Localization" =>
                "English fields are authoritative. Other languages fall back to English when blank. Use language codes installed in the game.",
            "Story rehearsal" =>
                "Rehearsal uses a copied draft and simulated state. Native outcomes are labeled; unresolved behavior pauses for your explicit result. It never opens player profiles.",
            _ =>
                "Save, validate and publish an immutable pack. Missing installed dependencies can allow export but prevent play. Restart SPT to load new packs, then choose a season when creating a seasonal character.",
        };
    }
}
