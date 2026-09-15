using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;

namespace WTT.Campaigns.Server;

public sealed record Metadata : IModMetadata, SPTarkov.Server.Web.IModBlazorMetadata
{
    public string? WWWRootUrl { get; init; } = "wtt-campaigns-creator-assets";
    public string? HomePage { get; init; } = "/wtt-campaigns/creator";
    public string? HomePageDescription { get; init; } = "Create, preview, and publish playable campaigns.";
    public static string DirectoryPath
    {
        get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!; }
    }

    public string ModGuid { get; init; } = "com.wtt.campaigns";
    public string Name { get; init; } = "WTT-Campaigns";
    public string Author { get; init; } = "CJ, WTT";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.8.0");
    public Range SptVersion { get; init; } = new("4.1.x");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, Range>? ModDependencies { get; init; } =
        new() { ["com.wtt.contentbackport"] = new(">=2.0.1"), ["com.wtt.commonlib"] = new(">=3.0.6") };
    public string? Url { get; init; }
    public string License { get; init; } = "MIT (code); game assets retain their original ownership";
}
