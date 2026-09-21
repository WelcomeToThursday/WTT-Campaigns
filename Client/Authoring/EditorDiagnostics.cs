using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace WTT.Campaigns.Client.Authoring;

// Bounded, read-only capture. Never enables the global profiler, takes a heap
// snapshot, enumerates scene objects, changes GC mode, or forces a collection.
internal static class EditorDiagnostics
{
    internal enum Area
    {
        FrameUpdate,
        LateUpdate,
        Hud,
        Environment,
        TerrainScan,
        Presentation,
        Geometry,
        Index,
        AssetIndex,
        ThumbnailPrepare,
        ThumbnailRender,
        OtherEditor,
        NativeCulling,
        NativeEffects,
        Count,
    }

    private static long _thumbnailHits,
        _thumbnailMisses;
    private static int _thumbnailTextures,
        _thumbnailQueued,
        _thumbnailLoads,
        _thumbnailWaits;
    private static double _thumbnailLoadMs,
        _thumbnailWaitMs;

    internal static void ThumbnailCache(long hits, long misses, int textures, int queued)
    {
        _thumbnailHits = hits;
        _thumbnailMisses = misses;
        _thumbnailTextures = textures;
        _thumbnailQueued = queued;
    }

    internal static void ThumbnailLoad(double milliseconds)
    {
        if (!_capturing)
            return;
        _thumbnailLoads++;
        _thumbnailLoadMs += milliseconds;
    }

    internal static void ThumbnailWait(double milliseconds)
    {
        if (!_capturing)
            return;
        _thumbnailWaits++;
        _thumbnailWaitMs += milliseconds;
    }

    private static readonly EditorTiming[] Timings = new EditorTiming[(int)Area.Count];
    private static readonly string[] CounterNames =
    {
        "Object Count",
        "Texture Count",
        "Mesh Count",
        "Material Count",
        "Render Texture Count",
    };
    private static readonly ProfilerRecorder[] Counters = new ProfilerRecorder[CounterNames.Length];
    private static bool _inRaid,
        _capturing,
        _failed,
        _allocationCounterAvailable;
    private static float _started,
        _lastSample,
        _lastFrame,
        _worstFrame;
    private static long _mainAllocated;
    private static int _collections,
        _frames,
        _stutters;
    private static Process? _process;
    internal static bool Enabled { get; set; }

    internal readonly struct Scope : IDisposable
    {
        private readonly bool _measure;
        private readonly Area _area;
        private readonly long _time,
            _allocated;

        internal Scope(Area area)
        {
            _measure = _capturing;
            _area = area;
            _time = _measure ? Stopwatch.GetTimestamp() : 0;
            _allocated = _measure && _allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : 0;
        }

        public void Dispose()
        {
            if (_measure && _capturing)
                Timings[(int)_area]
                    .Add(
                        Stopwatch.GetTimestamp() - _time,
                        _allocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - _allocated : 0
                    );
        }
    }

    internal static Scope Measure(Area area) => new(area);

    internal static void Tick(bool inRaid)
    {
        try
        {
            if (!inRaid || !Enabled)
            {
                if (_inRaid)
                    Stop();
                _inRaid = false;
                return;
            }
            if (_failed)
                return;
            if (!_inRaid)
            {
                _inRaid = true;
                Start();
                return;
            }
            if (!_capturing)
                return;
            var now = Time.realtimeSinceStartup;
            var frame = (now - _lastFrame) * 1000;
            _lastFrame = now;
            _worstFrame = Math.Max(_worstFrame, frame);
            if (frame >= 50)
                _stutters++;
            _frames++;
            if (now - _lastSample < 5)
                return;
            Report(now);
            if (now - _started >= 180)
            {
                Stop();
                Plugin.LogInfo("Editor memory capture finished after three minutes.");
            }
        }
        catch (Exception error)
        {
            _failed = true;
            Stop();
            Plugin.LogInfo("Editor memory capture unavailable: " + error.Message);
        }
    }

    private static void Start()
    {
        // Some Unity Mono players expose this API but always return zero.
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var probe = new byte[1024];
        _allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > allocationBefore;
        GC.KeepAlive(probe);
        _process = Process.GetCurrentProcess();
        for (var i = 0; i < Counters.Length; i++)
        {
            // Counter availability varies with the installed Unity player build.
            try
            {
                Counters[i] = ProfilerRecorder.StartNew(ProfilerCategory.Memory, CounterNames[i], 1);
            }
            catch
            {
                Counters[i] = default;
            }
        }
        Array.Clear(Timings, 0, Timings.Length);
        _started = _lastSample = _lastFrame = Time.realtimeSinceStartup;
        _mainAllocated = GC.GetAllocatedBytesForCurrentThread();
        _collections = GC.CollectionCount(0);
        _frames = _stutters = 0;
        _worstFrame = 0;
        _capturing = true;
        Plugin.LogInfo(
            "Editor memory capture started: five-second samples for three minutes; timings include nested scopes. Build "
                + typeof(EditorDiagnostics).Module.ModuleVersionId
        );
    }

    private static void Report(float now)
    {
        var elapsed = now - _lastSample;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var collections = GC.CollectionCount(0);
        var text = new StringBuilder(1400);
        _process!.Refresh();
        const double mb = 1024d * 1024;
        text.AppendFormat(
            System.Globalization.CultureInfo.InvariantCulture,
            "Editor memory +{0:F0}s: private={1} working={2} monoUsed={3:F1}MiB monoHeap={4:F1}MiB unityUsed={5:F1}MiB unityReserved={6:F1}MiB graphics={7} mainAlloc={8} gc={9} collections={10} fps={11:F1} maxFrame={12:F1}ms framesOver50ms={13}",
            now - _started,
            Memory(_process.PrivateMemorySize64),
            Memory(_process.WorkingSet64),
            Profiler.GetMonoUsedSizeLong() / mb,
            Profiler.GetMonoHeapSizeLong() / mb,
            Profiler.GetTotalAllocatedMemoryLong() / mb,
            Profiler.GetTotalReservedMemoryLong() / mb,
            Memory(Profiler.GetAllocatedMemoryForGraphicsDriver()),
            AllocationRate((allocated - _mainAllocated) / mb / elapsed),
            GarbageCollector.GCMode,
            collections - _collections,
            _frames / elapsed,
            _worstFrame,
            _stutters
        );
        for (var i = 0; i < Counters.Length; i++)
            text.Append(" | ")
                .Append(CounterNames[i])
                .Append('=')
                .Append(Counters[i].Valid && Counters[i].Count > 0 ? Counters[i].LastValue.ToString() : "unavailable");
        for (var i = 0; i < Timings.Length; i++)
        {
            var timing = Timings[i];
            if (timing.Calls == 0)
                continue;
            text.AppendFormat(
                System.Globalization.CultureInfo.InvariantCulture,
                " | {0}: calls={1} alloc={2} total={3:F1}ms max={4:F1}ms",
                (Area)i,
                timing.Calls,
                AllocationRate(timing.Allocated / mb / elapsed),
                timing.Ticks * 1000d / Stopwatch.Frequency,
                timing.MaxTicks * 1000d / Stopwatch.Frequency
            );
        }
        text.AppendFormat(
            System.Globalization.CultureInfo.InvariantCulture,
            " | thumbnails: hits={0} misses={1} textures={2} queued={3} loads={4}/{5:F1}ms waits={6}/{7:F1}ms",
            _thumbnailHits,
            _thumbnailMisses,
            _thumbnailTextures,
            _thumbnailQueued,
            _thumbnailLoads,
            _thumbnailLoadMs,
            _thumbnailWaits,
            _thumbnailWaitMs
        );
        _thumbnailLoads = _thumbnailWaits = 0;
        _thumbnailLoadMs = _thumbnailWaitMs = 0;
        Plugin.LogInfo(text.ToString());
        _lastSample = now;
        _mainAllocated = allocated;
        _collections = collections;
        _frames = _stutters = 0;
        _worstFrame = 0;
        Array.Clear(Timings, 0, Timings.Length);
    }

    private static string Memory(long bytes) =>
        bytes > 0 ? (bytes / (1024d * 1024)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "MiB" : "unavailable";

    private static string AllocationRate(double rate) =>
        _allocationCounterAvailable ? rate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "MiB/s" : "unavailable";

    internal static void Stop()
    {
        _capturing = false;
        for (var i = 0; i < Counters.Length; i++)
        {
            Counters[i].Dispose();
            Counters[i] = default;
        }
        _process?.Dispose();
        _process = null;
    }
}
