using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal static class NavigationTerrainEvidence
{
    private sealed class Capture
    {
        internal TerrainCollider Collider = null!;
        internal TerrainData Data = null!;
        internal NavigationCollisionCatalog.TerrainEntry Entry = null!;
        internal int Scene;
    }

    private static readonly List<Capture> Captures = new();
    private static NavigationCollisionCatalog? _catalog;
    private static bool _enabled,
        _verified;
    private static string _verification = "Native terrain evidence has not been verified";
    private static string _captureStatus = "No native terrain evidence captured; load the editor map after installing this update";

    internal static void Enable()
    {
        if (_enabled)
            return;
        _enabled = true;
        try
        {
            using var stream = typeof(NavigationTerrainEvidence).Assembly.GetManifestResourceStream(
                "WTT.Campaigns.Navigation.native-collision.json"
            );
            if (stream == null)
                throw new InvalidDataException("Missing native collision catalogue");
            using var reader = new StreamReader(stream);
            _catalog = JsonConvert.DeserializeObject<NavigationCollisionCatalog>(reader.ReadToEnd());
            var error = _catalog?.Validate() ?? "Empty native collision catalogue";
            if (error.Length > 0)
                throw new InvalidDataException(error);
            // Never adopt an already-loaded object: its original serialized identity
            // cannot be established after another mod/editor has replaced components.
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }
        catch (Exception error)
        {
            _catalog = null;
            _verification = error.Message;
            Plugin.Error(error);
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!EditorMode.Active || _catalog == null)
            return;
        try
        {
            var expected = 0;
            var captured = 0;
            foreach (var entry in _catalog.Terrains)
            {
                if (scene.path != entry.ScenePath || scene.buildIndex != entry.BuildIndex)
                    continue;
                expected++;
                var target = Find(scene, entry);
                if (!target)
                    continue;
                var colliders = target!.GetComponents<TerrainCollider>();
                if (colliders.Length != 1 || !Matches(colliders[0], entry))
                    continue;
                Captures.Add(
                    new Capture
                    {
                        Collider = colliders[0],
                        Data = colliders[0].terrainData,
                        Entry = entry,
                        Scene = scene.handle,
                    }
                );
                captured++;
            }
            if (expected > 0)
            {
                _captureStatus = $"Captured {captured}/{expected} native terrain collision owners in {scene.name}";
                Plugin.LogInfo("Navigation: " + _captureStatus);
            }
        }
        catch (Exception error)
        {
            Captures.RemoveAll(c => c.Scene == scene.handle);
            Plugin.Error(error);
        }
    }

    private static void OnSceneUnloaded(Scene scene) => Captures.RemoveAll(c => c.Scene == scene.handle);

    internal static object Describe() =>
        new
        {
            Verified = _verified,
            Capture = _captureStatus,
            Verification = _verification,
            Sources = _catalog?.Files,
        };

    internal static async UniTask Verify(CancellationToken token)
    {
        _verified = false;
        var catalog = _catalog;
        if (catalog == null)
            return;
        if (Captures.Count == 0)
        {
            _verification = _captureStatus;
            return;
        }
        var directory = Application.dataPath;
        var result = "";
        Exception? failure = null;
        try
        {
            result = await UniTask.RunOnThreadPool(() => catalog.VerifyFiles(directory), configureAwait: false, cancellationToken: token);
        }
        catch (Exception error)
        {
            failure = error;
        }
        // Both successful and faulted worker continuations return before touching
        // scene state or propagating cancellation into Unity resource cleanup.
        await UniTask.SwitchToMainThread();
        token.ThrowIfCancellationRequested();
        if (failure is OperationCanceledException)
            throw failure;
        if (failure != null)
            result = "Native collision file verification failed: " + failure.Message;
        _verification = result;
        _verified = result.Length == 0;
    }

    internal static bool? Read(TerrainCollider terrain, out string evidence)
    {
        var property = typeof(TerrainCollider).GetProperty("enableTreeColliders");
        if (property?.PropertyType == typeof(bool))
        {
            evidence = "Native runtime terrain collision property";
            return (bool?)property.GetValue(terrain);
        }
        evidence = _verified ? _captureStatus + "; no matching original terrain collider" : _verification;
        if (!_verified)
            return null;
        foreach (var capture in Captures)
            if (capture.Collider == terrain)
            {
                if (
                    terrain.gameObject.scene.handle != capture.Scene
                    || terrain.terrainData != capture.Data
                    || Find(terrain.gameObject.scene, capture.Entry) != terrain.transform
                    || !Matches(terrain, capture.Entry)
                )
                {
                    evidence = "Captured terrain identity, hierarchy or data changed";
                    return null;
                }
                evidence = "SHA-256 verified native scene/data/bindings; original collider captured at scene load";
                return capture.Entry.EnableTreeColliders;
            }
        return null;
    }

    private static Transform? Find(Scene scene, NavigationCollisionCatalog.TerrainEntry entry)
    {
        if (!scene.IsValid() || scene.path != entry.ScenePath || scene.buildIndex != entry.BuildIndex)
            return null;
        Transform? target = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == entry.Hierarchy[0].Name)
            {
                if (target)
                    return null; // Duplicate roots are ambiguous, not a name-based exemption.
                target = root.transform;
            }
        for (var i = 0; i < entry.Hierarchy.Length; i++)
        {
            var node = entry.Hierarchy[i];
            if (i > 0)
            {
                if (!target || node.Sibling >= target!.childCount)
                    return null;
                target = target.GetChild(node.Sibling);
            }
            if (
                !target
                || target!.name != node.Name
                || !Near(target.localPosition, node.Position)
                || !Near(target.localScale, node.Scale)
                || Quaternion.Angle(
                    target.localRotation,
                    new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
                ) > .001f
            )
                return null;
        }
        return target;
    }

    private static bool Matches(TerrainCollider collider, NavigationCollisionCatalog.TerrainEntry entry) =>
        collider
        && collider.GetComponents<TerrainCollider>().Length == 1
        && collider.terrainData
        && collider.terrainData.name == entry.DataName
        && collider.terrainData.heightmapResolution == entry.Resolution
        && collider.terrainData.treeInstanceCount == entry.TreeCount
        && Near(collider.terrainData.size, entry.Size);

    private static bool Near(Vector3 actual, float[] expected) =>
        (actual - new Vector3(expected[0], expected[1], expected[2])).sqrMagnitude <= .000001f;
}
