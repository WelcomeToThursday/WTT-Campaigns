using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using EFT.UI;
using EFT.UI.BattleTimer;
using JsonType;
using UnityEngine;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Spatial;

/// <summary>Adds extracts to the existing native scenario; never directly ends a raid.</summary>
internal sealed class LevelExtractRuntime : IDisposable
{
    private readonly List<ExfiltrationPoint> _points = new();
    private ExfiltrationController? _controller;
    private EndByExitTrigerScenario? _scenario;
    private LocalGame? _game;
    private Player? _player;
    private Dictionary<string, ExitTimerPanel>? _nativeTimers;
    private readonly Dictionary<string, ExitTimerPanel> _timers = new();
    private static readonly System.Reflection.FieldInfo TimerRegistry =
        typeof(ExtractionTimersPanel).GetField(
            "_timers",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        ) ?? throw new MissingFieldException(typeof(ExtractionTimersPanel).FullName, "_timers");

    internal void Apply(IEnumerable<MapLayerContent> content, Player player)
    {
        var exits = content.AsValueEnumerable().Where(c => c.Extract != null).ToArray();
        if (exits.Length == 0)
            return;
        _game =
            Singleton<AbstractGame>.Instance as LocalGame ?? throw new InvalidOperationException("Level extracts require a local raid.");
        _scenario = _game.GetComponent<EndByExitTrigerScenario>();
        _controller = ExfiltrationController.Instance;
        _player = player;
        if (!_scenario || _controller == null || string.IsNullOrEmpty(player.Profile.Info.EntryPoint))
            throw new InvalidOperationException("Native extraction is not ready.");
        var duration = _controller
            .ExfiltrationPoints.AsValueEnumerable()
            .Where(p => p && p.Settings.ExfiltrationType == EExfiltrationType.Individual && p.Settings.ExfiltrationTime > 0)
            .Select(p => p.Settings.ExfiltrationTime)
            .FirstOrDefault();
        if (duration <= 0)
            duration = 10;
        var names = _controller
            .ExfiltrationPoints.AsValueEnumerable()
            .Where(p => p)
            .Select(p => p.Settings.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var layer in exits)
        {
            var volume = layer.Extract!;
            if (volume.Location != ZoneRuntime.Location)
                throw new InvalidOperationException("Level extract belongs to another map.");
            using var hash = System.Security.Cryptography.SHA256.Create();
            var id = BitConverter
                .ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(layer.Source + "/" + volume.Id)))
                .Replace("-", "")
                .Substring(0, 24)
                .ToLowerInvariant();
            var root = new GameObject("Level extract " + layer.Source);
            root.SetActive(false);
            root.transform.SetPositionAndRotation(
                ZoneRuntime.Vector(volume.Position),
                Quaternion.Euler(ZoneRuntime.Vector(volume.Rotation))
            );
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = volume.Shape == "Sphere" ? Vector3.one * volume.Radius * 2 : ZoneRuntime.Vector(volume.Size);
            var point = root.AddComponent<ExfiltrationPoint>();
            _points.Add(point);
            root.SetActive(true); // Awake requires the authored box collider.
            if (volume.Shape == "Sphere")
            {
                box.enabled = false;
                var sphere = root.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = volume.Radius;
            }
            var name = volume.Name;
            var suffix = 2;
            while (!names.Add(name))
                name = volume.Name + " (" + suffix++ + ")";
            point.LoadSettings(
                new MongoID(id),
                new BackendExitTriggerSettings
                {
                    Name = name,
                    ExfiltrationType = EExfiltrationType.Individual,
                    ExfiltrationTime = duration,
                    EntryPoints = player.Profile.Info.EntryPoint,
                    Chance = 100,
                    PlayersCount = 0,
                },
                true
            );
            point.OnStartExtraction += _scenario.StartExtraction;
            point.OnCancelExtraction += _scenario.CancelExtraction;
            point.OnStatusChanged += _scenario.OnStatusChangedHandler;
            point.OnStatusChanged += _game.OnStatusChangedHandler;
            _controller.ExfiltrationPoints = _controller.ExfiltrationPoints.AsValueEnumerable().Append(point).ToArray();
        }
        AddTimers();
    }

    private void AddTimers()
    {
        if (!_game || _player == null || !MonoBehaviourSingleton<GameUI>.Instantiated)
            throw new InvalidOperationException("Native extract timers are not ready.");
        var panel = MonoBehaviourSingleton<GameUI>.Instance.TimerPanel;
        _nativeTimers = (Dictionary<string, ExitTimerPanel>)TimerRegistry.GetValue(panel);
        foreach (var point in _points)
        {
            var timer = UnityEngine.Object.Instantiate(panel._timerPanelTemplate, panel._container);
            _timers.Add(point.Settings.Name, timer);
            timer.Show(
                DateTime.UtcNow,
                _player.Profile.Info.Side,
                _nativeTimers.Count + 1,
                new System.Text.StringBuilder(),
                subscribe: true,
                _player.Profile.Id,
                point
            );
            _nativeTimers.Add(point.Settings.Name, timer);
            _game!.UpdateExfiltrationUi(point, contains: false, initial: true);
        }
    }

    public void Dispose()
    {
        if (_points.Count == 0)
            return;
        if (_controller != null)
            _controller.ExfiltrationPoints = _controller.ExfiltrationPoints.AsValueEnumerable().Where(p => !_points.Contains(p)).ToArray();
        foreach (var point in _points)
        {
            if (!point)
                continue;
            if (_scenario)
            {
                if (_player != null && point.Entered.Contains(_player))
                    _scenario!.CancelExtraction(point, _player);
                point.OnStartExtraction -= _scenario!.StartExtraction;
                point.OnCancelExtraction -= _scenario.CancelExtraction;
                point.OnStatusChanged -= _scenario.OnStatusChangedHandler;
            }
            if (_game)
                point.OnStatusChanged -= _game!.OnStatusChangedHandler;
            point.Disable();
            UnityEngine.Object.Destroy(point.gameObject);
        }
        _points.Clear();
        foreach (var pair in _timers)
        {
            if (_nativeTimers != null && _nativeTimers.TryGetValue(pair.Key, out var registered) && registered == pair.Value)
                _nativeTimers.Remove(pair.Key);
            if (!pair.Value)
                continue;
            pair.Value.Close();
            UnityEngine.Object.Destroy(pair.Value.gameObject);
        }
        _timers.Clear();
        _nativeTimers = null;
    }
}
