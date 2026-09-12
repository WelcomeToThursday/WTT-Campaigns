using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;

namespace WTT.Campaigns.Server.Web.Documentation;

public sealed record WikiPage(string Slug, string Title, string Summary, string Markdown, string Html);

public static partial class WikiDocumentation
{
    private const string ResourcePrefix = "WTT.Campaigns.Wiki.";
    private const string ExampleResource = ResourcePrefix + "example-story-introduction.json";
    private static readonly IReadOnlyDictionary<string, string> RootPageSlugs = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["README.md"] = "installation",
        ["RELEASE_NOTES.md"] = "release-notes",
        ["CONTRIBUTING.md"] = "contributing",
        ["THIRD_PARTY_NOTICES.md"] = "third-party-notices",
        ["Client/Resources/README.md"] = "local-ui-resources",
    };
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    private static readonly Lazy<IReadOnlyList<WikiPage>> LoadedPages = new(Load);

    public static IReadOnlyList<WikiPage> Pages
    {
        get { return LoadedPages.Value; }
    }

    public static WikiPage Home
    {
        get { return Page("Home"); }
    }

    public static string SidebarHtml
    {
        get { return Page("_Sidebar").Html; }
    }

    public static WikiPage Page(string? slug)
    {
        var requested = string.IsNullOrWhiteSpace(slug) ? "Home" : slug.Trim();
        return Pages.FirstOrDefault(page => string.Equals(page.Slug, requested, StringComparison.OrdinalIgnoreCase))
            ?? Pages.First(page => page.Slug == "Home");
    }

    public static IReadOnlyList<WikiPage> Search(string? query)
    {
        var terms = (query ?? "")
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length == 0)
        {
            return [];
        }

        return Pages
            .Where(page => IsGuide(page) && terms.All(term => SearchText(page).Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(page => terms.Count(term => page.Title.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<WikiPage> Load()
    {
        var assembly = typeof(WikiDocumentation).Assembly;
        var resources = assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var slugs = resources.Select(name => name[ResourcePrefix.Length..^3]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        slugs.Add("example-story-introduction");

        var pages = resources
            .Select(resource =>
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                var markdown = reader.ReadToEnd();
                var slug = resource[ResourcePrefix.Length..^3];
                return CreatePage(slug, markdown, slugs);
            })
            .ToList();
        using var exampleStream = assembly.GetManifestResourceStream(ExampleResource)!;
        using var exampleReader = new StreamReader(exampleStream);
        pages.Add(
            CreatePage(
                "example-story-introduction",
                "# Story introduction example\n\nThis is the complete format-1 story overlay shipped with this version of WTT-Campaigns.\n\n```json\n"
                    + exampleReader.ReadToEnd()
                    + "\n```\n",
                slugs
            )
        );
        return pages;
    }

    private static WikiPage CreatePage(string slug, string markdown, IReadOnlySet<string> slugs)
    {
        var title = Title(markdown, slug);
        var summary = Summary(markdown, title);
        var rewritten = RewriteLinks(markdown, slug, slugs);
        var html = Markdown
            .ToHtml(rewritten, Pipeline)
            .Replace("<a href=\"https://", "<a target=\"_blank\" rel=\"noopener noreferrer\" href=\"https://", StringComparison.Ordinal);
        return new WikiPage(slug, title, summary, markdown, html);
    }

    private static bool IsGuide(WikiPage page)
    {
        return page.Slug is not "README" and not "_Sidebar" and not "_Footer";
    }

    private static string SearchText(WikiPage page)
    {
        return page.Title + " " + page.Summary + " " + page.Markdown;
    }

    private static string RewriteLinks(string markdown, string pageSlug, IReadOnlySet<string> slugs)
    {
        return MarkdownLink()
            .Replace(
                markdown,
                match =>
                    match.Groups["prefix"].Value + RewriteUrl(match.Groups["url"].Value, pageSlug, slugs) + match.Groups["suffix"].Value
            );
    }

    private static string RewriteUrl(string url, string pageSlug, IReadOnlySet<string> slugs)
    {
        if (url.StartsWith('#') || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return
                absolute.Host == "127.0.0.1" && absolute.AbsolutePath.Equals("/wtt-campaigns/creator", StringComparison.OrdinalIgnoreCase)
                ? "/wtt-campaigns/creator"
                : url;
        }

        var anchorIndex = url.IndexOf('#');
        var path = (anchorIndex < 0 ? url : url[..anchorIndex]).Replace('\\', '/');
        var anchor = anchorIndex < 0 ? "" : url[anchorIndex..];
        var normalized = path;
        while (normalized.StartsWith("../", StringComparison.Ordinal))
        {
            normalized = normalized[3..];
        }

        if (RootPageSlugs.TryGetValue(normalized, out var rootSlug))
        {
            return "/wtt-campaigns/docs/" + rootSlug + anchor;
        }

        if (normalized.Equals("examples/story-introduction.json", StringComparison.OrdinalIgnoreCase))
        {
            return "/wtt-campaigns/docs/example-story-introduction" + anchor;
        }

        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var slug = Path.GetFileNameWithoutExtension(normalized);
            if (normalized.StartsWith("contributing/", StringComparison.OrdinalIgnoreCase))
            {
                slug = "contributor-" + slug;
            }
            else if (pageSlug.StartsWith("contributor-", StringComparison.OrdinalIgnoreCase) && !normalized.Contains('/'))
            {
                slug = "contributor-" + slug;
            }

            if (slugs.Contains(slug))
            {
                return "/wtt-campaigns/docs/" + Uri.EscapeDataString(slug) + anchor;
            }
        }

        return url;
    }

    private static string Title(string markdown, string fallback)
    {
        var match = Heading().Match(markdown);
        return match.Success ? InlineMarkup().Replace(match.Groups[1].Value, "").Trim() : fallback.Replace('-', ' ');
    }

    private static string Summary(string markdown, string title)
    {
        var paragraphs = Regex.Split(markdown, @"\r?\n\s*\r?\n");
        foreach (var paragraph in paragraphs)
        {
            var candidate = paragraph.Trim();
            if (
                candidate.Length == 0
                || candidate.StartsWith('#')
                || candidate.StartsWith('|')
                || candidate.StartsWith('-')
                || candidate.StartsWith('[')
            )
            {
                continue;
            }

            candidate = MarkdownLinkText().Replace(candidate, "$1");
            candidate = InlineMarkup().Replace(candidate, "");
            candidate = Regex.Replace(candidate, @"\s+", " ").Trim();
            if (candidate.Length > 0 && !string.Equals(candidate, title, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Length <= 180 ? candidate : candidate[..177].TrimEnd() + "…";
            }
        }

        return "Open this WTT-Campaigns guide.";
    }

    [GeneratedRegex(@"(?<prefix>!?\[[^\]]*\]\()(?<url>[^)\s]+)(?<suffix>(?:\s+[""'][^)]*[""'])?\))")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^#\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex Heading();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex MarkdownLinkText();

    [GeneratedRegex(@"[*_`~]")]
    private static partial Regex InlineMarkup();
}
