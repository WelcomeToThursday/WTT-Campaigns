using System.Reflection;
using Mono.Cecil;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class UnityToolkitChecks
{
    // Read assemblies and exercise pure text code; never instantiate the Unity plugin or start EFT.
    internal static void Run(string sptRoot, string clientPath)
    {
        var toolkitDir = Path.GetFullPath(Path.Combine(sptRoot, "BepInEx/plugins/UnityToolkit"));
        clientPath = Path.GetFullPath(clientPath);
        using var client = AssemblyDefinition.ReadAssembly(clientPath);
        using var toolkit = AssemblyDefinition.ReadAssembly(Path.Combine(toolkitDir, "UnityToolkit.dll"));
        var dependency = client
            .MainModule.Types.Single(t => t.Name == "Plugin")
            .CustomAttributes.Single(a =>
                a.AttributeType.Name == "BepInDependency" && (string)a.ConstructorArguments[0].Value == "com.arys.unitytoolkit"
            );
        var installed = toolkit.MainModule.Types.SelectMany(t => t.CustomAttributes).Single(a => a.AttributeType.Name == "BepInPlugin");
        Check((string)installed.ConstructorArguments[0].Value == "com.arys.unitytoolkit", "Toolkit plugin identity");
        Check(
            Version.Parse((string)installed.ConstructorArguments[2].Value)
                >= Version.Parse((string)dependency.ConstructorArguments[1].Value),
            "Installed Toolkit meets the client minimum"
        );
        foreach (var name in new[] { "ZLinq", "ZString", "UniTask" })
        {
            using var library = AssemblyDefinition.ReadAssembly(Path.Combine(toolkitDir, name + ".dll"));
            Check(client.MainModule.AssemblyReferences.Single(r => r.Name == name).FullName == library.Name.FullName, name + " identity");
            Check(
                client.MainModule.GetMemberReferences().OfType<MethodReference>().Any(m => m.DeclaringType.Scope.Name == name),
                name + " is used by the compiled client"
            );
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(clientPath)!, name + ".dll")), name + " stays owned by UnityToolkit");
        }

        Check(
            !client
                .MainModule.GetMemberReferences()
                .OfType<MethodReference>()
                .Any(m => m.DeclaringType.FullName == "System.Linq.Enumerable"),
            "Client queries use ZLinq throughout, including nested lambdas and async methods"
        );

        var context = new ClientAssemblyContext(sptRoot, clientPath);
        var installedLinq = context.LoadFromAssemblyName(new AssemblyName("ZLinq"));
        Check(Path.GetDirectoryName(installedLinq.Location) == toolkitDir, "Runtime checks use the installed Unity ZLinq build");
        var assembly = context.LoadFromAssemblyPath(clientPath);
        UniTaskChecks.Run(client, assembly, Check);
        var changesType = assembly.GetType("WTT.Campaigns.Client.Story.StoryChapterChanges")!;
        var changes = Activator.CreateInstance(changesType, nonPublic: true)!;
        StoryChapterNotificationChecks.Run(
            Check,
            changesType
                .GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<StoryResponse, IReadOnlyList<(StoryChapter Chapter, string Status)>>>(changes),
            changesType.GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Action>(changes)
        );
        var build = assembly
            .GetType("WTT.Campaigns.Client.Story.StorySubtitleText")!
            .GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<StorySequence[], float, Func<string, string>, string, string>>();
        StorySequence[] subtitles =
        [
            new()
            {
                Key = "first",
                Start = 1,
                End = 3,
            },
            new()
            {
                Key = "second",
                Start = 2,
                End = 4,
            },
            new()
            {
                Key = "empty",
                Start = 2,
                End = 3,
            },
        ];
        Func<string, string> localize = static key =>
            key switch
            {
                "first" => "Hello 世界",
                "second" => "Next line",
                _ => "",
            };
        Check(build([], 1, localize, "old") == "", "No subtitles clears prior text");
        Check(build(subtitles, 0, localize, "") == "", "Before first subtitle");
        Check(build(subtitles, 1, localize, "") == "Hello 世界", "Inclusive start and localization");
        var overlap = build(subtitles, 2, localize, "");
        Check(overlap == "Hello 世界\nNext line\n", "Overlaps preserve sequence order and empty localized entries");
        Check(ReferenceEquals(overlap, build(subtitles, 2.5f, localize, overlap)), "Unchanged text reuses its string");
        Check(build(subtitles, 3, localize, overlap) == "Next line", "Exclusive end");
        Check(build(subtitles, 4, localize, overlap) == "", "Playback end clears text");
        Check(build(subtitles, float.NaN, localize, overlap) == "", "Invalid time matches no subtitles");
        var longText = new string('x', 100_000);
        Check(build(subtitles, 1, _ => longText, "") == longText, "Pooled buffer can grow for long subtitles");
        try
        {
            build(subtitles, 1, _ => throw new InvalidOperationException("localization failure"), "");
            throw new Exception("Localization failure must propagate");
        }
        catch (InvalidOperationException) { }
        Check(build(subtitles, 2, localize, "") == overlap, "Builder remains usable after localization failure");
        for (var i = 0; i < 100; i++)
        {
            build(subtitles, 2, localize, overlap);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            build(subtitles, 2, localize, overlap);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "Unchanged subtitle composition allocates no managed memory after warmup");
        Console.WriteLine(
            "UnityToolkit assembly, client LINQ audit, chapter and subtitle checks passed; 500 unchanged compositions allocated "
                + allocated
                + " bytes."
        );
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("UnityToolkit check failed: " + name);
        }
    }
}
