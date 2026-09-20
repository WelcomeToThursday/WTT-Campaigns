using System.Globalization;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Navigation;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private NavigationSurvey? _navigationSurvey;
    private CancellationTokenSource? _navigationScan;

    private NavigationBuildStamp NavigationStamp() =>
        new(_session?.RaidId ?? "", _layoutId, _session?.ContentVersion ?? -1, SceneNavigation.Revision);

    private bool NavigationAvailable() =>
        _open
        && EditorMode.Ready
        && _session is { Retired: false, Conflict: null }
        && Layout != null
        && !AiPreviewBusy
        && !_walking
        && !_session.Previewing
        && _drag == null
        && !Catalog.Placing;

    private string RunNavigationCommand(string[] args)
    {
        if (args[0] == "status")
            return (_navigationPaint?.Status ?? "Manual navigation: paint Add or Block in Windows → Navigation, then build.")
                + $"\nSurface snapshot: {_navigationNativeOverlay?.Triangles ?? 0} triangles · "
                + (NavigationSurfaceRenderer.ProjectOntoGround ? "ground-projected coverage" : "world depth-tested overlay")
                + (_navigationDisplayFloor.HasValue ? $" · floor {_navigationDisplayFloor.Value:0.00}" : " · all floors");
        if (args[0] == "cancel")
        {
            _navigationPaint?.Cancel();
            _navigationScan?.Cancel();
            _navigationHealthWork?.Cancel();
            return "Cancellation requested; waiting for owned-resource cleanup.";
        }
        if (!NavigationAvailable())
            return "Open a layout and end playtests, placement or dragging before editing navigation.";
        switch (args[0])
        {
            case "build":
                BuildNavigationPaint();
                return "";
            case "restore":
                _navigationHealthInvalid = true;
                _navigationPaint?.Clear();
                _navigationNativeOverlay?.Clear();
                return "Owned additions and blocks cleared. Native navigation remains loaded.";
            case "view":
                ViewNavigationPaint(args.Length > 1 && args[1] == "floor");
                return _navigationFeedback;
            case "hide":
                _navigationPaintHidden = true;
                return "Navigation overlays hidden.";
            case "scan":
                if (_navigationScan != null || !CanPaintNavigation)
                    throw new InvalidOperationException("Wait for navigation work to finish and clear the preview first.");
                if (Layout?.Navigation?.Cells.Count is not > 0)
                    throw new InvalidOperationException("Paint an area first; scans are restricted to its bounds.");
                ScanNavigationPaint().Forget();
                return "";
            case "issues":
                return _navigationSurvey?.IssuesPage(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1)
                    ?? "Scan the painted region first.";
            case "select":
                var index = int.Parse(args[1], CultureInfo.InvariantCulture) - 1;
                if (_navigationSurvey == null || index >= _navigationSurvey.Issues.Count)
                    return "That source issue does not exist.";
                var issue = _navigationSurvey.Issues[index];
                if (!issue.Target)
                    return "The source object is no longer loaded: " + issue.Path;
                _navigationBrush = "";
                EnterSceneSelection();
                Catalog.SelectSceneTarget(issue.Target!);
                FrameSceneSelectionCore();
                return issue.Reason + "\n" + issue.Path;
            case "report":
                var folder = Path.Combine(BepInEx.Paths.ConfigPath, "WTT-Campaigns", "navigation-diagnostics");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(
                    folder,
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture) + "-manual.json"
                );
                File.WriteAllText(
                    path,
                    JsonConvert.SerializeObject(
                        new
                        {
                            Mode = "Manual additions and blocks",
                            Recipe = Layout?.Navigation,
                            Status = _navigationPaint?.Status,
                            Scan = _navigationSurvey?.Summary(),
                            Health = _navigationHealth == null
                                ? null
                                : new
                                {
                                    _navigationHealth.Origin,
                                    _navigationHealth.Samples,
                                    _navigationHealth.Truncated,
                                    _navigationHealth.Unverified,
                                    Counts = _navigationHealth.Counts,
                                    Stale = NavigationHealthStale,
                                    Status = _navigationHealthStatus,
                                },
                            Issues = _navigationSurvey?.Issues.AsValueEnumerable().Select(i => new { i.Path, i.Reason }).ToArray(),
                        },
                        Formatting.Indented
                    )
                );
                return "Manual navigation report saved: " + path;
            default:
                throw new ArgumentException("Unknown manual navigation command.");
        }
    }

    private async UniTask ScanNavigationPaint()
    {
        var lifetime = _navigationScan = new CancellationTokenSource();
        var stamp = NavigationStamp();
        try
        {
            var region = NavigationPaintSources.Region(Layout!.Navigation!, UnityEngine.AI.NavMesh.GetSettingsByID(0).agentHeight);
            _navigationFeedback = "Scanning the painted region…";
            var survey = await NavigationSurvey.Collect(region, lifetime.Token);
            if (!stamp.Equals(NavigationStamp()))
                throw new OperationCanceledException();
            _navigationSurvey = survey;
            _navigationFeedback = survey.Summary();
            LogNavigationFeedback(_navigationFeedback, survey.Issues.Count > 0);
        }
        catch (Exception error)
        {
            _navigationFeedback = error is OperationCanceledException ? "Painted-region scan cancelled." : error.Message;
            LogNavigationFeedback(_navigationFeedback, error is not OperationCanceledException);
        }
        finally
        {
            if (_navigationScan == lifetime)
                _navigationScan = null;
            lifetime.Dispose();
            _navigationRefreshAt = 0;
        }
    }

    private static void LogNavigationFeedback(string message, bool warning)
    {
        if (warning)
            Plugin.LogWarning("Navigation: " + message);
        else
            Plugin.LogInfo("Navigation: " + message);
    }

    private Vector3 NavigationPoint()
    {
        if (_mode == "AI" && Selected is { } point)
            return ZoneRuntime.Vector(point.Position);
        if (TryRouteFloor(_flyPosition, 200, out var floor))
            return floor.point;
        throw new InvalidOperationException("Move the camera above a solid floor.");
    }
}
