using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Client.Authoring.Editor;

public sealed partial class RaidEditor
{
    private EditorContentMode ContentMode => EditorContentRules.Mode(_session?.Definition, _layoutId);
    private bool MissionContent => ContentMode == EditorContentMode.Mission;

    private bool ContentToolAllowed(string tool) => EditorContentRules.ToolAllowed(ContentMode, tool, _session?.Definition?.Story != null);

    private void PresentContentMode()
    {
        var view = _view!;
        view.ContentMode = ContentMode;
        view.HasStory = _session?.Definition?.Story != null;
        view.Text("WorkspaceTitle", EditorContentRules.Title(ContentMode).ToUpperInvariant());
        foreach (var tool in RaidEditorView.ToolIds)
            view.Visible(tool, ContentToolAllowed(tool));
        view.Caption("Routes", MissionContent ? "Routes" : "Extracts");
        view.Caption("MapExit", MissionContent ? "+ Exit" : "+ Extract");
        view.PresentToolTitle("Routes", MissionContent ? "ROUTES" : "EXTRACTS");
        view.Caption("RouteFrame", MissionContent ? "Frame waypoint" : "Frame extract");
        foreach (var id in new[] { "AiObserve", "AiPlaytest", "TestCheckpoints", "AiPlaytestGear", "MapWalkStart" })
            view.Visible(id, MissionContent);
        foreach (var id in new[] { "MapStart", "MapCheckpoint" })
            if (!MissionContent)
                view.Visible(id, false);
        if (!MissionContent)
        {
            view.Visible("MapOrderGroup", false);
            view.Visible("ZoneCreateScope", false);
            if (_mode == "Routes")
                view.Text("RouteGuide", "EXTRACTS / Adds an extract alongside the map's existing extracts.");
        }
        if (MissionContent)
            view.Visible("MapNormalRaidGroup", false);
    }
}
