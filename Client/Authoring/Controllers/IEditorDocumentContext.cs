using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal interface IEditorDocumentContext
{
    string LayoutId { get; set; }
    string LibraryKey { get; set; }
    string ToolId { get; }
    void ReportFeedback(string message, ConsoleSeverity severity = ConsoleSeverity.Info);
    List<(string Id, string Label)> Rows { get; }
    string SelectionId { get; set; }
    RaidEditorSession? Session { get; }
    RaidEditorView? View { get; }
    MapLayout? Layout { get; }
    void Refresh();
    void Refresh(bool geometry);
}
