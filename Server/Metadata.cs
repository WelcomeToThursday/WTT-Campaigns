using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;

namespace SeasonalPerks.Server;

public sealed record Metadata : IModMetadata, SPTarkov.Server.Web.IModBlazorMetadata
{
    public string? WWWRootUrl { get; init; } = "seasonal-creator-assets";
    public string? HomePage { get; init; } = "/wtt-seasonal/creator";
    public string? HomePageDescription { get; init; } = "Create, preview, and publish playable seasons.";
    public static string DirectoryPath
    {
        get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!; }
    }

    public string ModGuid { get; init; } = "com.cj.seasonalperks";
    public string Name { get; init; } = "Seasonal Perks";
    public string Author { get; init; } = "CJ-SPT";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.5.1");
    public Range SptVersion { get; init; } = new("~4.1.3");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT (code); game assets retain their original ownership";
}
