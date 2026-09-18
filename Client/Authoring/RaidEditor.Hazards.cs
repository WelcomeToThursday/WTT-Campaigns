using System.Threading;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    internal static bool HazardsSuppressed => EditorMode.Active ? !AiPlaytestActive : Instance && Instance!._open;

    private async Task BeginPlaytestHazards(IEnumerable<SeasonZone> zones, CancellationToken token)
    {
        EndPlaytestHazards();
        try
        {
            foreach (var zone in zones)
            {
                if (zone.Hazard == null || zone.Location != _session?.Location)
                    continue;
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName(zone.Scene);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException("Hazard scene is unavailable: " + zone.Scene);
                var root = ZoneRuntime.Volume(zone);
                _playtestHazards.Add(root);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                await root.AddComponent<HazardRuntime>().Initialize(zone, token);
            }
        }
        catch
        {
            EndPlaytestHazards();
            throw;
        }
    }

    private void EndPlaytestHazards()
    {
        foreach (var root in _playtestHazards)
        {
            if (!root)
                continue;
            root.GetComponent<HazardRuntime>()?.Clear();
            root.SetActive(false);
            Destroy(root);
        }
        _playtestHazards.Clear();
    }

    private void BindHazards(RaidEditorView view)
    {
        view.Button("SniperPlaySound", () => SetSniperSound(play: view.IsChecked("SniperPlaySound")));
        view.Button("SniperSuppressed", () => SetSniperSound(suppressed: view.IsChecked("SniperSuppressed")));
        foreach (var kind in HazardRules.Kinds)
        {
            var captured = kind;
            view.Button("Add" + kind, () => AddHazard(captured));
        }
    }

    private void SetSniperSound(bool? play = null, bool? suppressed = null)
    {
        if (_session == null || _session.Previewing || AiPreviewBusy)
            return;
        try
        {
            EditPoint(point =>
            {
                if (point is not SeasonZone { Hazard: { Kind: "Sniper" } } zone)
                    return;
                if (play.HasValue)
                    zone.Hazard.PlayShotSound = play.Value;
                if (suppressed.HasValue)
                    zone.Hazard.SuppressedShots = suppressed.Value;
            });
            Refresh();
        }
        catch (Exception error)
        {
            _notice = error.Message;
            Plugin.Error(error);
        }
    }

    private static string HazardDescription(string kind) =>
        HazardRules.Label(kind)
        + "\n"
        + (
            kind switch
            {
                "Minefield" => "Native landmine damage on entry and further movement inside the area.",
                "Claymore" => "Single-use directional mine. The forward line shows blast direction; the box is its trigger area.",
                "Sniper" => "Native border sniper fire, with an initial warning shot and escalating lethality.",
                "BarbedWire" => "Native contact damage and movement slowdown. The box defines wire coverage.",
                _ => "",
            }
        )
        + "\nInert while editing, observing and walking through. Armed during Playtest and active missions. Reset restores hazards.";

    private void AddHazard(string kind)
    {
        if (!EditorMode.Ready || _session?.Definition == null || _session.Previewing || _session.Conflict != null || AiPreviewBusy)
            return;
        if (!_zoneCreateShared && Layout == null)
        {
            _notice = "Select a layout before placing a hazard, or choose Shared scope.";
            Refresh();
            return;
        }
        try
        {
            var position = Aim(out var scene) ?? _player!.Transform.position;
            if (scene.Length == 0)
                scene = PlayerScene();
            var size = kind switch
            {
                "Claymore" => new SpatialVector
                {
                    X = 2,
                    Y = 1,
                    Z = 3,
                },
                "BarbedWire" => new SpatialVector
                {
                    X = 5,
                    Y = 1,
                    Z = 1,
                },
                _ => new SpatialVector
                {
                    X = 10,
                    Y = 4,
                    Z = 10,
                },
            };
            position += Vector3.up * size.Y * .5f;
            var zone = new SeasonZone
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 24),
                Name = HazardRules.Label(kind),
                Location = _session.Location,
                Scene = scene,
                Position = ZoneRuntime.Vector(position),
                Rotation = new SpatialVector { Y = _flyRotation.eulerAngles.y },
                Size = size,
                Uses = new(),
                LayoutId = _zoneCreateShared ? "" : _layoutId,
                Hazard = new HazardSettings { Kind = kind },
            };
            _session.Edit(s => s.Zones.Add(zone));
            _selected = zone.Id;
            _mode = "Hazards";
            _notice = "Placed " + HazardRules.Label(kind) + ". Move, rotate and resize its area in the inspector.";
            Refresh();
        }
        catch (Exception error)
        {
            _notice = error.Message;
            Plugin.Error(error);
        }
    }
}
