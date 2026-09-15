using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class PreviewFreezeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definition = new SeasonDefinition { Name = "Original layout" };
        var session = new RaidEditorSession("woods") { Definition = definition, Baseline = RaidEditorSession.Copy(definition) };
        session.Edit(d => d.Name = "Authored layout");
        var revision = session.ContentVersion;
        session.Previewing = true;
        session.Edit(d => d.Name = "Unexpected preview edit");
        session.Undo(false);
        check(
            session.Definition!.Name == "Authored layout" && session.ContentVersion == revision,
            "Preview freezes direct editing and undo without changing the layout revision"
        );
        session.Previewing = false;
        session.Undo(false);
        check(session.Definition.Name == "Original layout", "Reset restores editing with the original undo history");
    }
}
