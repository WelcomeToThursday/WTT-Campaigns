using System.Text.RegularExpressions;
using WTT.Campaigns.Server.Web.Documentation;

namespace WTT.Campaigns.Tests;

internal static partial class WikiDocumentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var pages = WikiDocumentation.Pages;
        check(pages.Count >= 33, "Wiki and versioned supporting pages are embedded in the mod webpage");
        check(WikiDocumentation.Home.Title == "WTT-Campaigns Wiki", "Documentation home keeps its wiki title");
        check(
            WikiDocumentation.Home.Markdown.Contains("SPT 4.1.x", StringComparison.Ordinal)
                && !WikiDocumentation.Pages.Any(page => page.Markdown.Contains("SPT 4.1." + "3", StringComparison.Ordinal)),
            "Embedded documentation consistently identifies SPT 4.1.x compatibility"
        );
        check(
            WikiDocumentation.Home.Html.Contains("href=\"/wtt-campaigns/docs/characters\"", StringComparison.Ordinal),
            "Wiki page links stay inside the mod webpage"
        );
        check(
            WikiDocumentation.SidebarHtml.Contains("href=\"/wtt-campaigns/docs/story-system\"", StringComparison.Ordinal),
            "Wiki sidebar links stay inside the mod webpage"
        );
        check(
            WikiDocumentation.Page("season-creator").Html.Contains("href=\"/wtt-campaigns/creator\"", StringComparison.Ordinal),
            "Creator guide uses the current local server address"
        );
        check(
            WikiDocumentation.Home.Html.Contains("href=\"/wtt-campaigns/docs/installation#requirements\"", StringComparison.Ordinal),
            "Installation links use the README embedded with this mod version"
        );
        check(
            WikiDocumentation
                .Page("story-authoring")
                .Html.Contains("href=\"/wtt-campaigns/docs/example-story-introduction\"", StringComparison.Ordinal),
            "The story JSON example is available as a local embedded page"
        );
        check(
            WikiDocumentation
                .Page("contributing")
                .Html.Contains("href=\"/wtt-campaigns/docs/contributor-build-deployment\"", StringComparison.Ordinal),
            "Supporting contributor documents use their embedded copies"
        );
        check(
            WikiDocumentation.Search("profile recovery").Any(page => page.Slug == "typed-models-and-profile-storage"),
            "Guide search finds matching body text"
        );
        check(WikiDocumentation.Search("story").Count > 1, "Guide search returns multiple relevant pages");
        check(WikiDocumentation.Page("missing") == WikiDocumentation.Home, "Unknown documentation route returns the documentation home");

        var unresolvedLocalLinks = pages
            .SelectMany(page => HtmlLink().Matches(page.Html).Select(match => match.Groups[1].Value))
            .Where(url =>
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && url.Contains(".md", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        check(unresolvedLocalLinks.Length == 0, "Every local documentation link resolves to an embedded page");
    }

    [GeneratedRegex("href=\\\"([^\\\"]+)")]
    private static partial Regex HtmlLink();
}
