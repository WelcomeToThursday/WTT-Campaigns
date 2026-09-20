using System.Globalization;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private int _navigationIssue;
    private float _navigationRefreshAt;
    private string _navigationFeedback = "",
        _navigationPanelLayout = "";
    private bool CanEditNavigationRecipe =>
        NavigationAvailable()
        && _session?.Busy == false
        && _navigationScan == null
        && _navigationHealthWork == null
        && _navigationPaint?.Busy != true
        && _navigationPaint?.Active != true;

    private void BindNavigationPanel(RaidEditorView view)
    {
        void Button(string id, Action action) => view.Button(id, () => NavigationPanelAction(action));
        void Command(string id, params string[] args) => Button(id, () => NavigationPanelCommand(args));
        Button("NavBuild", BuildNavigationPaint);
        Command("NavCancel", "cancel");
        Command("NavRestore", "restore");
        Command("NavScan", "scan");
        Command("NavReport", "report");
        Command("NavViewAll", "view", "all");
        Command("NavViewFloor", "view", "floor");
        Command("NavHide", "hide");
        Button("NavIssuePrevious", () => _navigationIssue--);
        Button("NavIssueNext", () => _navigationIssue++);
        Button(
            "NavIssueSelect",
            () => NavigationPanelCommand(new[] { "select", (_navigationIssue + 1).ToString(CultureInfo.InvariantCulture) })
        );
        BindNavigationPainting(view);
        BindNavigationHealth(view);
    }

    private void NavigationPanelAction(Action action)
    {
        try
        {
            _navigationFeedback = "";
            action();
        }
        catch (Exception error)
        {
            _navigationFeedback = error.Message;
            LogNavigationFeedback(error.Message, true);
        }
        _navigationRefreshAt = 0;
        RefreshNavigationPanel();
    }

    private void NavigationPanelCommand(string[] args)
    {
        var message = RunNavigationCommand(args);
        if (message.Length == 0)
            return;
        _navigationFeedback = message;
        LogNavigationFeedback(message, false);
    }

    private void EditNavigationRecipe(Func<MapNavigationRecipe?, MapNavigationRecipe?> edit)
    {
        if (!CanEditNavigationRecipe)
            throw new InvalidOperationException("Wait for synchronization and clear the preview before editing navigation.");
        var id = Layout!.Id;
        var recipe = edit(Layout.Navigation == null ? null : RaidEditorSession.Copy(Layout.Navigation));
        var errors = MapNavigationRules.Errors(recipe);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join("\n", errors));
        _session!.Edit(d => d.MapLayouts.Find(l => l.Id == id)!.Navigation = recipe);
        _navigationFeedback = "Navigation edits saved. Build Preview applies only your painted areas.";
    }

    private void RefreshNavigationPanel()
    {
        if (!_open || _view?.Valid != true || !_view.Windows.IsOpen("Navigation") || UnityEngine.Time.unscaledTime < _navigationRefreshAt)
            return;
        _navigationRefreshAt = UnityEngine.Time.unscaledTime + .25f;
        var view = _view;
        if (_navigationPanelLayout != Layout?.Id)
        {
            _navigationPanelLayout = Layout?.Id ?? "";
            _navigationFeedback = "";
            _navigationIssue = 0;
            _navigationSurvey = null;
            ClearNavigationHealth();
            _navigationStroke = null;
            _navigationBrush = "";
            _navigationFloor = null;
            _navigationLinkStart = _navigationPathStart = null;
        }
        view.Text("NavLayout", Layout?.Name ?? "Choose a layout in the dedicated editor");
        view.Text("NavState", "Native navigation · manual editing");
        view.Text("NavStatus", "Paint physical surfaces. Build Preview adds only painted areas and applies your blocks.");
        view.Text("NavFeedback", _navigationFeedback);
        view.Element("NavScan").SetEnabled(CanPaintNavigation && Layout?.Navigation?.Cells.Count > 0);
        view.Element("NavReport").SetEnabled(NavigationAvailable());
        var count = _navigationSurvey?.Issues.Count ?? 0;
        _navigationIssue = Math.Clamp(_navigationIssue, 0, Math.Max(0, count - 1));
        view.Text(
            "NavIssueCount",
            _navigationSurvey == null ? "No painted-region scan yet"
                : count == 0 ? "No source issues"
                : $"Source issue {_navigationIssue + 1} of {count}"
        );
        view.Text(
            "NavIssue",
            count == 0 ? "" : _navigationSurvey!.Issues[_navigationIssue].Reason + "\n" + _navigationSurvey.Issues[_navigationIssue].Path
        );
        view.Element("NavIssuePrevious").SetEnabled(_navigationIssue > 0);
        view.Element("NavIssueNext").SetEnabled(_navigationIssue + 1 < count);
        view.Element("NavIssueSelect").SetEnabled(NavigationAvailable() && count > 0);
        RefreshNavigationPainting();
        RefreshNavigationHealth();
        view.Element("NavCancel").SetEnabled(_navigationPaint?.Busy == true || _navigationScan != null || _navigationHealthWork != null);
    }
}
