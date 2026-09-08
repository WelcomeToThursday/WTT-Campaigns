using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Server.Web.Authoring;

// Presentation only: identities in drafts and published content are never rewritten.
public static class ReferenceNames
{
    public static string Localized(string kind, string id, string fallback, Func<string, string?> lookup)
    {
        var suffixes = kind == "traders" ? new[] { " Nickname", " FullName", " Name", " name" } : new[] { " Name", " name" };
        return suffixes.Select(s => lookup(id + s)).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? fallback;
    }

    public static string Resolve(SeasonDefinition season, string kind, string id, Func<string, string, string> installed)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "None selected";
        }

        return StoryAuthoring.Choices(season, kind).FirstOrDefault(c => c.Id == id).Name ?? installed(kind, id);
    }

    public static string Label(SeasonDefinition season, object record, Func<string, string, string> installed)
    {
        return record switch
        {
            StoryQuest q => Resolve(season, "quests", q.QuestId, installed),
            StoryEntryPoint e => Resolve(season, "dialogs", e.DialogId, installed)
                + " · "
                + StoryAuthoring.Friendly(e.Kind)
                + (e.StartPoint.Length > 0 ? " · " + e.StartPoint : ""),
            StoryVariable v when season.Story?.Dialogs.FirstOrDefault(d => d.MainVariable == v.Id) is { } dialog => "Phase: "
                + StoryAuthoring.Label(dialog)
                + " · "
                + v.Scope,
            StoryRaidBinding b when b.Kind == "Collectible" => "Collect "
                + Resolve(season, "items", b.ItemId, installed)
                + " · "
                + b.Location,
            _ => StoryAuthoring.Label(record),
        };
    }
}
