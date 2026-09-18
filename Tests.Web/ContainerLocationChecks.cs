using System.Reflection;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Models.Spt.Config;
using WTT.Campaigns.Server.Editor;

namespace WTT.Campaigns.Web.Tests;

internal static class ContainerLocationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var config = (LocationConfig)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LocationConfig));
        typeof(LocationConfig)
            .GetProperty(nameof(LocationConfig.StaticLootMultiplier))!
            .SetValue(
                config,
                new Dictionary<string, double>
                {
                    ["labyrinth"] = 1.25,
                    ["factory4_day"] = 2,
                    ["sandbox_high"] = 3,
                }
            );
        // Only the native configuration lookup runs; no server, database or profile is created.
        var generator = new LocationLootGenerator(null!, null!, null!, null!, null!, null!, null!, null!, config, null!, null!, null!);
        var multiplier = typeof(LocationLootGenerator).GetMethod(
            "GetStaticLootMultiplierForLocation",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var normalize = typeof(SceneContainerLoot).GetMethod("NativeLocationId", BindingFlags.Static | BindingFlags.NonPublic)!;
        var failed = false;
        try
        {
            multiplier.Invoke(generator, ["Labyrinth"]);
        }
        catch (TargetInvocationException e) when (e.InnerException is KeyNotFoundException)
        {
            failed = true;
        }
        check(failed, "Native container generation reproduces the property-name lookup failure");
        foreach (
            var (property, id, expected) in new[]
            {
                ("Labyrinth", "labyrinth", 1.25),
                ("Factory4Day", "factory4_day", 2d),
                ("SandboxHigh", "sandbox_high", 3d),
            }
        )
        {
            var actual = (string)normalize.Invoke(null, [property])!;
            check(
                actual == id && (double)multiplier.Invoke(generator, [actual])! == expected,
                "Container loot maps " + property + " to its actual native configuration key"
            );
        }
    }
}
