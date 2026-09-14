using HarmonyLib;
using Koenigz.PerfectCulling;
using Koenigz.PerfectCulling.EFT;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WTT.Campaigns.Client.Authoring;

// The native sampler runs in Update, before the editor applies its final LateUpdate pose.
// Keep its cached observer and camera distance samples aligned without moving the player.
internal sealed partial class EditorEnvironment : IDisposable
{
    private static EditorEnvironment? _active;
    private static bool _patched;
    private readonly Camera _camera;
    private readonly bool _occlusion;
    private readonly HashSet<PerfectCullingCamera> _observers = new();
    private readonly EditorTerrainVisibility _terrain = new();
    private EditorSceneVisibility? _sceneVisibility;
    private bool _terrainDiscoveryPending = true;
    private TOD_Time? _time;
    private TOD_CycleParameters? _cycle;
    private DateTime _date;
    private bool _locked;
    private Vector3 _position;
    private Quaternion _rotation;

    internal EditorEnvironment(Camera camera, Func<GameObject, bool> hidden)
    {
        Enable();
        _camera = camera;
        _occlusion = camera.useOcclusionCulling;
        _position = camera.transform.position;
        _rotation = camera.transform.rotation;
        camera.useOcclusionCulling = false;
        _active = this;
        SceneManager.sceneLoaded += SceneLoaded;
        try
        {
            if (EditorMode.Ready)
                _sceneVisibility = new EditorSceneVisibility(hidden);
            Sync();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal bool Previewing => _time;
    internal bool Available => TODSkyProvider.IsAvailable && TODSkyProvider.Instance.CurrentTime;
    internal float Hour => Available ? TODSkyProvider.Instance.Cycle.Hour : 0;

    internal void SetHour(float hour)
    {
        if (!Available)
            return;
        var sky = TODSkyProvider.Instance;
        if (_time != sky.CurrentTime || _cycle != sky.Cycle)
        {
            RestoreTime();
            _time = sky.CurrentTime;
            _cycle = sky.Cycle;
            _date = _cycle.DateTime;
            _locked = _time.LockCurrentTime;
        }
        _time.LockCurrentTime = true;
        _cycle.Hour = Mathf.Repeat(hour, 24);
    }

    internal void RestoreTime()
    {
        try
        {
            if (_time && _cycle != null)
            {
                // Use the still-running raid clock so time spent editing is not rewound.
                try
                {
                    _cycle.DateTime =
                        !_locked && _time!.GameDateTime != null
                            ? _time.GameDateTime.CalculateTaxonomyDate(_time.GetTaxonomyTodSkyDate())
                            : _date;
                }
                finally
                {
                    _time!.LockCurrentTime = _locked;
                }
            }
        }
        finally
        {
            _time = null;
            _cycle = null;
        }
    }

    internal void Pose(Vector3 position, Quaternion rotation)
    {
        _position = position;
        _rotation = rotation;
        Sync();
    }

    private void Observer(PerfectCullingCamera? observer)
    {
        if (!observer)
            return;
        if (_observers.Add(observer!))
        {
            observer!.SetDirty();
        }
        observer!.ObservePosition = _position;
    }

    private void Sync()
    {
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.Environment);
        if (!_camera)
            return;
        _camera.transform.SetPositionAndRotation(_position, _rotation);
        // Trigger-based occlusion switches can turn this back on after Open.
        _camera.useOcclusionCulling = false;
        if (_terrainDiscoveryPending)
        {
            using var terrainDiagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.TerrainScan);
            _terrainDiscoveryPending = false;
            foreach (var terrain in Resources.FindObjectsOfTypeAll<Terrain>())
                if (terrain && terrain.gameObject.scene.IsValid() && terrain.gameObject.scene.isLoaded)
                    _terrain.Observe(terrain);
            foreach (var lod in Resources.FindObjectsOfTypeAll<TerrainLod>())
                if (lod && lod.gameObject.scene.IsValid() && lod.gameObject.scene.isLoaded)
                    _terrain.Observe(lod);
            _sceneVisibility?.Discover();
        }
        _terrain.Show();
        Observer(_camera.GetComponent<PerfectCullingCamera>());
        var sampler = PerfectCullingCrossSceneSampler.Instance;
        if (sampler)
            Observer(sampler.CullingCamera);
    }

    // Native resource enumeration stalls large maps. Discover once on opening,
    // then only after a scene loads; visibility enforcement uses the cached objects.
    private void SceneLoaded(Scene scene, LoadSceneMode mode) => _terrainDiscoveryPending = true;

    private static void BeforeSampling(out EditorDiagnostics.Scope __state)
    {
        __state = EditorDiagnostics.Measure(EditorDiagnostics.Area.NativeCulling);
        _active?.Sync();
    }

    private static void AfterSampling(EditorDiagnostics.Scope __state) => __state.Dispose();

    private static void AfterCameraSettings() => _active?.Sync();

    private static void AfterObserver(PerfectCullingCamera __instance)
    {
        if (_active != null && _active._observers.Contains(__instance))
            _active.Observer(__instance);
    }

    private static void Enable()
    {
        if (_patched)
            return;
        var harmony = new Harmony("com.wtt.campaigns.editor.environment");
        var before = new HarmonyMethod(typeof(EditorEnvironment), nameof(BeforeSampling));
        var after = new HarmonyMethod(typeof(EditorEnvironment), nameof(AfterSampling));
        harmony.Patch(
            AccessTools.PropertyGetter(typeof(EFT.Weather.WeatherController), nameof(EFT.Weather.WeatherController.WeatherCurve)),
            prefix: new HarmonyMethod(typeof(EditorEnvironment), nameof(WeatherCurve))
        );
        harmony.Patch(
            AccessTools.Method(typeof(PerfectCullingCrossSceneSampler), nameof(PerfectCullingCrossSceneSampler.Update)),
            prefix: before,
            postfix: after
        );
        harmony.Patch(
            AccessTools.Method(typeof(PerfectCullingCamera), nameof(PerfectCullingCamera.CamPreCull)),
            prefix: before,
            postfix: after
        );
        harmony.Patch(AccessTools.Method(typeof(CullingManager), nameof(CullingManager.CameraRender)), prefix: before, postfix: after);
        harmony.Patch(
            AccessTools.Method(typeof(CullingManager), nameof(CullingManager.ScheduleCullingJobs)),
            prefix: before,
            postfix: after
        );
        harmony.Patch(
            AccessTools.Method(typeof(OcclusionCullingSwitcher), nameof(OcclusionCullingSwitcher.UpdateCameraSettings)),
            postfix: new HarmonyMethod(typeof(EditorEnvironment), nameof(AfterCameraSettings))
        );
        harmony.Patch(
            AccessTools.Method(typeof(PerfectCullingCamera), nameof(PerfectCullingCamera.Update)),
            postfix: new HarmonyMethod(typeof(EditorEnvironment), nameof(AfterObserver))
        );
        _patched = true;
    }

    public void Dispose()
    {
        SceneManager.sceneLoaded -= SceneLoaded;
        if (_active == this)
            _active = null;
        try
        {
            RestoreTime();
        }
        catch (Exception e)
        {
            Plugin.Error(e);
        }
        finally
        {
            RestoreWeather();
            _terrain.Dispose();
            if (_camera)
                _camera.useOcclusionCulling = _occlusion;
            foreach (var entry in _observers)
                if (entry)
                {
                    entry.ObservePosition = entry.transform.position;
                    entry.SetDirty();
                }
            _observers.Clear();
            _sceneVisibility?.Dispose();
            _sceneVisibility = null;
        }
    }
}
