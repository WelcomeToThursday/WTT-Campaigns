using WTT.Campaigns.Server.Seasons;

namespace WTT.Campaigns.Tests;

internal static class IsolatedLocaleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var registry = new IsolatedLocaleLayers();
        var english = new Dictionary<string, string> { ["shared"] = "Original" };
        var original = new Dictionary<string, string>(english);
        var old = registry.Install("en", english, new Dictionary<string, string> { ["shared"] = "Old test", ["new"] = "Test item" });
        var next = registry.Install("en", english, new Dictionary<string, string> { ["shared"] = "New test", ["new"] = "Replacement item" });
        old.Dispose();
        check(english["shared"] == "New test" && english["new"] == "Replacement item", "Retiring a reset's old snapshot preserves the replacement locale");
        next.Dispose();
        check(english.Count == original.Count && english["shared"] == original["shared"], "Ending a reset restores pre-test locale and removes temporary keys");
        old.Dispose(); next.Dispose();
        check(english.Count == original.Count, "Repeated locale cleanup is harmless");
        var one = registry.Install("en", english, new Dictionary<string, string> { ["shared"] = "First" });
        var two = registry.Install("en", english, new Dictionary<string, string> { ["shared"] = "Second" });
        two.Dispose();
        check(english["shared"] == "First", "Failed replacement registration restores the still-active test locale");
        one.Dispose();
        check(english["shared"] == "Original", "Final locale cleanup restores the original value");
    }
}
