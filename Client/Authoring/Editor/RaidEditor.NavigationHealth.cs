using Cysharp.Threading.Tasks;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Navigation;
using WTT.Campaigns.Client.Authoring.Views;

namespace WTT.Campaigns.Client.Authoring.Editor;

public sealed partial class RaidEditor
{
    private CancellationTokenSource? _navigationHealthWork;
    private NavigationHealthScan? _navigationHealth;
    private NavigationSurfaceRenderer? _navigationHealthOverlay;
    private NavigationBuildStamp _navigationHealthStamp;
    private bool _navigationHealthPreview;
    private bool _navigationHealthInvalid;
    private int _navigationHealthMask;
    private float? _navigationHealthFloor;
    private string _navigationHealthStatus =
        "Scan active navigation within 24 m of the ground beneath the camera. Checks are sampled, not a guarantee.";
    private string _navigationHealthFilter = "All";

    private bool NavigationHealthStale =>
        _navigationHealth != null
        && (
            _navigationHealthInvalid
            || !_navigationHealthStamp.Equals(NavigationStamp())
            || _navigationHealthPreview != (_navigationPaint?.Active == true)
            || _navigationHealthFloor != _navigationDisplayFloor
            || _navigationPaint?.Busy == true
            || _navigationStroke != null
        );

    private void BindNavigationHealth(RaidEditorView view)
    {
        view.Button(
            "NavHealthScan",
            () =>
                NavigationPanelAction(() =>
                {
                    if (
                        !NavigationAvailable()
                        || _session?.Busy == true
                        || _navigationHealthWork != null
                        || _navigationScan != null
                        || _navigationPaint?.Busy == true
                        || _navigationStroke != null
                    )
                        throw new InvalidOperationException("Finish the current edit or navigation operation first.");
                    if (!TryRouteFloor(_flyPosition, 200, out var ground))
                        throw new InvalidOperationException("Move the camera above a solid floor to choose the scan origin.");
                    var origin = ground.point;
                    _navigationBrush = "";
                    ScanNavigationHealth(origin).Forget();
                })
        );
        view.Button("NavHealthClear", () => NavigationPanelAction(ClearNavigationHealth));
        var filters = new[] { "All", "Support", "Clearance", "Disconnected", "Slope", "Narrow", "Height" };
        for (var i = 0; i < filters.Length; i++)
        {
            var name = filters[i];
            var mask = i == 0 ? 0 : 1 << (i - 1);
            view.Button(
                "NavHealth" + name,
                () =>
                {
                    _navigationHealthFilter = name;
                    _navigationHealthMask = mask;
                    if (_navigationHealthOverlay != null)
                        _navigationHealthOverlay.IssueFilter = mask;
                    _navigationRefreshAt = 0;
                }
            );
        }
    }

    private async UniTask ScanNavigationHealth(Vector3 origin)
    {
        var lifetime = _navigationHealthWork = new CancellationTokenSource();
        var stamp = NavigationStamp();
        var preview = _navigationPaint?.Active == true;
        var floor = _navigationDisplayFloor;
        try
        {
            _navigationHealthStatus = "Scanning active navigation…";
            var result = await NavigationHealthScan.Run(
                origin,
                floor,
                lifetime.Token,
                count =>
                {
                    if (
                        !stamp.Equals(NavigationStamp())
                        || preview != (_navigationPaint?.Active == true)
                        || !NavigationAvailable()
                        || _navigationPaint?.Busy == true
                        || floor != _navigationDisplayFloor
                    )
                        lifetime.Cancel();
                    _navigationHealthStatus = $"Scanning… {count} samples (maximum {NavigationHealthScan.Limit}).";
                }
            );
            lifetime.Token.ThrowIfCancellationRequested();
            if (
                !stamp.Equals(NavigationStamp())
                || preview != (_navigationPaint?.Active == true)
                || !NavigationAvailable()
                || _navigationPaint?.Busy == true
                || floor != _navigationDisplayFloor
            )
                throw new OperationCanceledException();
            _navigationHealth = result;
            _navigationHealthInvalid = false;
            _navigationHealthStamp = stamp;
            _navigationHealthPreview = preview;
            _navigationHealthFloor = floor;
            _navigationHealthOverlay ??= new() { Diagnostics = true };
            _navigationHealthOverlay.IssueFilter = _navigationHealthMask;
            _navigationHealthOverlay.Capture(
                new() { vertices = result.Vertices.ToArray(), indices = result.Indices.ToArray() },
                floor,
                result.Issues.ToArray()
            );
            _navigationHealthStatus =
                $"{result.Samples} sampled triangles · 24 m around ({result.Origin.x:0.0}, {result.Origin.y:0.0}, {result.Origin.z:0.0})."
                + (
                    result.Truncated
                        ? " Limit reached; remaining areas unchecked. Move closer and scan again."
                        : " Areas outside this scan are unchecked."
                )
                + "\nSupport "
                + result.Counts[0]
                + " · clearance "
                + result.Counts[1]
                + " · disconnected "
                + result.Counts[2]
                + "\nSlope "
                + result.Counts[3]
                + " · narrow "
                + result.Counts[4]
                + " · height "
                + result.Counts[5]
                + "\nCounts overlap. Connectivity is a two-way navigation route to the scan origin. Physical checks are samples; doors can change results.";
            if (result.Unverified > 0)
                _navigationHealthStatus +=
                    $"\n{result.Unverified} unverified samples (query limit or no matching infantry surface); excluded from issue counts.";
            _navigationPaintHidden = false;
            _view?.ShowNavigationOverlay();
        }
        catch (Exception error)
        {
            if (_navigationHealthWork == lifetime)
                _navigationHealthStatus =
                    error is OperationCanceledException ? "Scan cancelled or scene changed. Scan again when ready." : error.Message;
        }
        finally
        {
            if (_navigationHealthWork == lifetime)
                _navigationHealthWork = null;
            lifetime.Dispose();
            _navigationRefreshAt = 0;
        }
    }

    private void RefreshNavigationHealth()
    {
        _navigationHealthInvalid = NavigationHealthStale;
        var ready =
            NavigationAvailable()
            && _session?.Busy == false
            && _navigationHealthWork == null
            && _navigationScan == null
            && _navigationPaint?.Busy != true
            && _navigationStroke == null;
        _view!.Element("NavHealthScan").SetEnabled(ready);
        _view.Element("NavHealthClear").SetEnabled(_navigationHealth != null || _navigationHealthWork != null);
        _view.Text(
            "NavHealthStatus",
            (NavigationHealthStale ? "STALE — scene, preview or floor changed. Scan again.\n" : "")
                + "Filter: "
                + _navigationHealthFilter
                + "\n"
                + _navigationHealthStatus
        );
    }

    private void DrawNavigationHealth(Camera camera)
    {
        if (_navigationHealthOverlay == null)
            return;
        _navigationHealthOverlay.Stale = NavigationHealthStale;
        _navigationHealthOverlay.Draw(camera);
    }

    private void ClearNavigationHealth()
    {
        _navigationHealthWork?.Cancel();
        _navigationHealthWork = null;
        _navigationHealth = null;
        _navigationHealthMask = 0;
        _navigationHealthInvalid = false;
        _navigationHealthOverlay?.Dispose();
        _navigationHealthOverlay = null;
        _navigationHealthFilter = "All";
        _navigationHealthStatus =
            "Scan active navigation within 24 m of the ground beneath the camera. Checks are sampled, not a guarantee.";
    }
}
