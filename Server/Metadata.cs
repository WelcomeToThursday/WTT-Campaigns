using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace SeasonalPerks.Server;

public sealed record Metadata : IModMetadata
{
    public static string DirectoryPath => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    public string ModGuid { get; init; } = "com.cj.seasonalperks";
    public string Name { get; init; } = "Seasonal Perks";
    public string Author { get; init; } = "CJ-SPT";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.1.23");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.3");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT (code); game assets retain their original ownership";
}
